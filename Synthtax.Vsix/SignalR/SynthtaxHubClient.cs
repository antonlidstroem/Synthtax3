using System.Net.WebSockets;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Synthtax.Shared.SignalR;
using Synthtax.Vsix.Auth;

namespace Synthtax.Vsix.SignalR;

// ═══════════════════════════════════════════════════════════════════════════
// ConnectionState  —  VSIX-sidan av anslutningens status
// ═══════════════════════════════════════════════════════════════════════════

public enum HubConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthError     // Token saknas / utgången — väntar på inloggning
}

// ═══════════════════════════════════════════════════════════════════════════
// ISynthtaxHubClient  —  kontrakt mot resten av VSIX
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hanterar SignalR-anslutningen mot Synthtax backend och routar
/// inkommande events till registrerade handlers.
/// </summary>
public interface ISynthtaxHubClient : IAsyncDisposable
{
    HubConnectionState State { get; }

    // ── Events ─────────────────────────────────────────────────────────────
    event EventHandler<AnalysisUpdatedPayload>   AnalysisUpdated;
    event EventHandler<IssueStatusChangedPayload> IssueStatusChanged;
    event EventHandler<LicenseChangedPayload>     LicenseChanged;
    event EventHandler<HubConnectionState>        ConnectionStateChanged;

    // ── Livscykel ──────────────────────────────────────────────────────────

    /// <summary>Starta anslutning. Anropas efter lyckad inloggning.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Stoppa anslutning. Anropas vid utloggning eller paketstängning.</summary>
    Task StopAsync(CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// SynthtaxHubClient  —  implementering
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hanterar en persistent SignalR WebSocket-anslutning mot Synthtax backend.
///
/// <para><b>Reconnect-strategi (exponentiell backoff):</b>
/// <list type="bullet">
///   <item>Försök 1: 0 s</item>
///   <item>Försök 2: 2 s</item>
///   <item>Försök 3: 5 s</item>
///   <item>Försök 4: 10 s</item>
///   <item>Försök 5+: 30 s (tak)</item>
/// </list>
/// </para>
///
/// <para><b>Auth-integration:</b>
/// HubConnection konfigureras med en <c>AccessTokenProvider</c> som
/// läser färsk token från <see cref="AuthTokenService"/> inför varje request.
/// Om token saknas eller är utgången sätts state till <see cref="HubConnectionState.AuthError"/>
/// och anslutningsförsöket avbryts tills ny token finns.</para>
///
/// <para><b>Trådsäkerhet:</b>
/// Events raisas alltid på ThreadPool-tråd. VSIX-prenumeranter ansvarar
/// för att marsha till UI-tråd (<c>JoinableTaskFactory.SwitchToMainThreadAsync</c>).</para>
/// </summary>
public sealed class SynthtaxHubClient : ISynthtaxHubClient
{
    private readonly AuthTokenService           _auth;
    private readonly ILogger<SynthtaxHubClient> _logger;
    private HubConnection?                      _connection;
    private CancellationTokenSource             _cts = new();
    private HubConnectionState                  _state = HubConnectionState.Disconnected;

    // Exponentiell backoff-intervall (index = reconnect-försöksnummer, 0-baserat)
    private static readonly TimeSpan[] BackoffIntervals =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)   // tak
    ];

    public HubConnectionState State => _state;

    // ── Events ─────────────────────────────────────────────────────────────
    public event EventHandler<AnalysisUpdatedPayload>?    AnalysisUpdated;
    public event EventHandler<IssueStatusChangedPayload>? IssueStatusChanged;
    public event EventHandler<LicenseChangedPayload>?     LicenseChanged;
    public event EventHandler<HubConnectionState>?        ConnectionStateChanged;

    public SynthtaxHubClient(AuthTokenService auth, ILogger<SynthtaxHubClient> logger)
    {
        _auth   = auth;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Livscykel
    // ═══════════════════════════════════════════════════════════════════════

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_state is HubConnectionState.Connected or HubConnectionState.Connecting)
            return;

        if (!_auth.IsAuthenticated)
        {
            SetState(HubConnectionState.AuthError);
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _connection = BuildConnection();
        RegisterHandlers();
        await ConnectWithRetryAsync(_cts.Token);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _cts.CancelAsync();

        if (_connection is not null)
        {
            await _connection.StopAsync(ct);
            await _connection.DisposeAsync();
            _connection = null;
        }

        SetState(HubConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ═══════════════════════════════════════════════════════════════════════
    // Anslutningslogik
    // ═══════════════════════════════════════════════════════════════════════

    private HubConnection BuildConnection()
    {
        var hubUrl = _auth.ApiBaseUrl.TrimEnd('/') + "/hubs/synthtax";

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                // JWT-token injiceras i varje WebSocket-request
                opts.AccessTokenProvider = () =>
                    Task.FromResult(_auth.GetCachedToken());

                // Tillåt WebSocket, Long Polling som fallback
                opts.Transports =
                    Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                    Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

                opts.SkipNegotiation = false;
            })
            .WithAutomaticReconnect(new ExponentialBackoffRetryPolicy())
            .ConfigureLogging(lb => lb.SetMinimumLevel(LogLevel.Warning))
            .Build();
    }

    private void RegisterHandlers()
    {
        if (_connection is null) return;

        // ── Server → Client events ────────────────────────────────────────
        _connection.On<AnalysisUpdatedPayload>(HubMethods.AnalysisUpdated, payload =>
        {
            _logger.LogInformation(
                "SignalR ← AnalysisUpdated: org={OrgId} +{New} -{Resolved}",
                payload.OrganizationId, payload.NewIssuesCount, payload.ResolvedIssuesCount);
            AnalysisUpdated?.Invoke(this, payload);
        });

        _connection.On<IssueStatusChangedPayload>(HubMethods.IssueStatusChanged, payload =>
        {
            _logger.LogDebug("SignalR ← IssueStatusChanged: issue={IssueId}", payload.IssueId);
            IssueStatusChanged?.Invoke(this, payload);
        });

        _connection.On<LicenseChangedPayload>(HubMethods.LicenseChanged, payload =>
        {
            _logger.LogInformation(
                "SignalR ← LicenseChanged: {Old}→{New}", payload.OldPlan, payload.NewPlan);
            LicenseChanged?.Invoke(this, payload);
        });

        _connection.On<HeartbeatPayload>(HubMethods.Heartbeat, async payload =>
        {
            // Ponga heartbeat tillbaka till servern
            try
            {
                await _connection.InvokeAsync(
                    HubMethods.AcknowledgeHeartbeat, payload.ServerTime);
            }
            catch { /* Heartbeat-fel är icke-kritiska */ }
        });

        // ── Anslutningslivscykel ──────────────────────────────────────────
        _connection.Reconnecting += ex =>
        {
            _logger.LogWarning("SignalR återsansluter: {Reason}", ex?.Message);
            SetState(HubConnectionState.Reconnecting);
            return Task.CompletedTask;
        };

        _connection.Reconnected += connId =>
        {
            _logger.LogInformation("SignalR återsansluten med ID {ConnId}", connId);
            SetState(HubConnectionState.Connected);
            return Task.CompletedTask;
        };

        _connection.Closed += ex =>
        {
            _logger.LogWarning("SignalR stängd: {Reason}", ex?.Message ?? "normal");
            SetState(HubConnectionState.Disconnected);
            return Task.CompletedTask;
        };
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        SetState(HubConnectionState.Connecting);
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_auth.IsAuthenticated)
                {
                    SetState(HubConnectionState.AuthError);
                    return;
                }

                await _connection!.StartAsync(ct);
                SetState(HubConnectionState.Connected);
                _logger.LogInformation("SignalR ansluten: {ConnId}", _connection.ConnectionId);
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (WebSocketException ex)
            {
                _logger.LogWarning("SignalR WebSocket-fel: {Msg}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignalR anslutningsfel (försök {Attempt})", attempt + 1);
            }

            SetState(HubConnectionState.Reconnecting);
            var delay = BackoffIntervals[Math.Min(attempt, BackoffIntervals.Length - 1)];
            _logger.LogDebug("SignalR väntar {Delay} innan nästa försök.", delay);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { return; }

            attempt++;
        }
    }

    private void SetState(HubConnectionState newState)
    {
        if (_state == newState) return;
        _state = newState;
        ConnectionStateChanged?.Invoke(this, newState);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ExponentialBackoffRetryPolicy  —  WithAutomaticReconnect helper
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Exponentiell backoff-policy för SignalR:s inbyggda automatiska återsanslutning.
/// Används som komplement till den manuella ConnectWithRetryAsync-loopen.
/// </summary>
internal sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        // null = sluta försöka (aldrig — vi försöker alltid)
        var idx = (int)Math.Min(retryContext.PreviousRetryCount, Delays.Length - 1);
        return Delays[idx];
    }
}
