using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Synthtax.Realtime.Contracts;
using Synthtax.Vsix.Auth;

namespace Synthtax.Vsix.Services;

/// <summary>
/// Hanterar SignalR-anslutningen till Synthtax-backend.
///
/// <para><b>Anslutningslogik:</b>
/// <list type="bullet">
///   <item>Ansluter till <c>{ApiBaseUrl}/hubs/analysis</c> med JWT i Authorization-header.</item>
///   <item>Automatisk återanslutning med exponentiell backoff: 2s → 5s → 10s → 30s → 60s (max).</item>
///   <item>Vid 401-svar (token utgången): publicerar <see cref="ConnectionStateChanged"/>
///         med <see cref="RealtimeConnectionState.Failed"/> utan ytterligare retry.</item>
/// </list>
/// </para>
///
/// <para><b>Trådsäkerhet:</b>
/// Alla C#-events publiceras via dispatcher-invoke på UI-tråden om en
/// <see cref="System.Windows.Threading.Dispatcher"/> är tillgänglig.
/// Prenumeranter behöver inte ta hänsyn till tråd.</para>
///
/// <para><b>Livscykel:</b>
/// <list type="number">
///   <item>Skapas av <c>SynthtaxPackage.InitializeAsync()</c>.</item>
///   <item><see cref="StartAsync"/> anropas efter lyckad inloggning.</item>
///   <item><see cref="StopAsync"/> anropas vid utloggning eller paketdispose.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SynthtaxRealtimeService : IAsyncDisposable
{
    private readonly AuthTokenService _auth;
    private readonly ILogger          _logger;

    private HubConnection? _connection;
    private CancellationTokenSource? _cts;
    private ConnectionStateSnapshot _currentState = ConnectionStateSnapshot.Disconnected();
    private int _reconnectAttempt;

    // Exponentiell backoff-sekvens (sekunder)
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    // ── Publika events ─────────────────────────────────────────────────────

    /// <summary>SignalR-anslutningsstatus har förändrats.</summary>
    public event EventHandler<ConnectionStateSnapshot>? ConnectionStateChanged;

    /// <summary>En analyssession är klar — backloggen har uppdaterats.</summary>
    public event EventHandler<AnalysisUpdatedEventArgs>? AnalysisUpdated;

    /// <summary>Ett enskilt nytt issue har skapats.</summary>
    public event EventHandler<IssueCreatedEventArgs>? IssueCreated;

    /// <summary>Ett issue har auto-stängts.</summary>
    public event EventHandler<IssueClosedEventArgs>? IssueClosed;

    /// <summary>Hälsopoängen har förändrats.</summary>
    public event EventHandler<HealthScoreUpdatedEventArgs>? HealthScoreUpdated;

    // ── Publik status ──────────────────────────────────────────────────────

    public RealtimeConnectionState State => _currentState.State;
    public bool IsConnected => _currentState.State == RealtimeConnectionState.Connected;

    public SynthtaxRealtimeService(AuthTokenService auth, ILogger logger)
    {
        _auth   = auth;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Start / Stop
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Startar SignalR-anslutningen.
    /// Anropas efter lyckad inloggning — token måste finnas i AuthTokenService.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_connection is not null)
            await StopAsync(); // Rensa eventuell gammal anslutning

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _reconnectAttempt = 0;

        await ConnectWithRetryAsync(_cts.Token);
    }

    /// <summary>Stänger anslutningen permanent (utloggning / dispose).</summary>
    public async Task StopAsync()
    {
        _cts?.Cancel();

        if (_connection is not null)
        {
            try { await _connection.StopAsync(); }
            catch { /* Tyst vid force-stop */ }
            await _connection.DisposeAsync();
            _connection = null;
        }

        SetState(ConnectionStateSnapshot.Disconnected());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Anslutningslogik
    // ═══════════════════════════════════════════════════════════════════════

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SetState(ConnectionStateSnapshot.Connecting());

            try
            {
                _connection = BuildHubConnection();
                RegisterHandlers(_connection);

                await _connection.StartAsync(ct);

                // Lyckad anslutning — gå med i org-grupp
                var orgId = GetOrgIdFromToken();
                if (orgId is not null)
                    await _connection.InvokeAsync(
                        HubMethodNames.JoinOrgGroup,
                        orgId.Value.ToString("D"),
                        ct);

                _reconnectAttempt = 0;
                SetState(ConnectionStateSnapshot.Connected());
                _logger.LogInformation("SignalR connected to Synthtax hub.");

                // Vänta tills anslutningen tappas
                await WaitForDisconnectAsync(_connection, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break; // Normal avstängning
            }
            catch (Exception ex) when (IsAuthError(ex))
            {
                // 401 — token ogiltig, ingen återanslutning
                _logger.LogWarning("SignalR auth failed — token expired or invalid.");
                SetState(ConnectionStateSnapshot.Failed("Token ogiltig. Logga in igen."));
                return;
            }
            catch (Exception ex)
            {
                _reconnectAttempt++;
                var delay = GetReconnectDelay(_reconnectAttempt);

                _logger.LogWarning(
                    ex,
                    "SignalR disconnected (attempt {Attempt}). Reconnecting in {Delay}s.",
                    _reconnectAttempt, delay.TotalSeconds);

                SetState(ConnectionStateSnapshot.Reconnecting(
                    _reconnectAttempt, delay, ex.Message));

                try { await Task.Delay(delay, ct); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (_connection is not null)
                {
                    await _connection.DisposeAsync();
                    _connection = null;
                }
            }
        }
    }

    private HubConnection BuildHubConnection()
    {
        var hubUrl    = BuildHubUrl();
        var tokenFactory = () =>
        {
            var token = _auth.GetCachedToken();
            return new ValueTask<string?>(token);
        };

        return new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.AccessTokenProvider = tokenFactory;

                // WebSocket → Long Polling fallback
                opts.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets
                                | Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

                // Stäng av certifikatvalidering i debug-mode
#if DEBUG
                opts.HttpMessageHandlerFactory = _ =>
                    new System.Net.Http.HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                    };
#endif
            })
            .WithAutomaticReconnect(new CustomReconnectPolicy())
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Warning);
            })
            .Build();
    }

    private void RegisterHandlers(HubConnection conn)
    {
        // AnalysisUpdated — komplett batch
        conn.On<AnalysisUpdatedEvent>(
            HubMethodNames.AnalysisUpdated,
            payload =>
            {
                _logger.LogInformation(
                    "AnalysisUpdated: {New} new, {Closed} closed, score={Score:F1}",
                    payload.NewIssueCount, payload.ClosedIssueCount, payload.HealthScore);
                DispatchEvent(AnalysisUpdated, new AnalysisUpdatedEventArgs { Payload = payload });
            });

        // IssueCreated — enskilt nytt issue
        conn.On<IssueCreatedEvent>(
            HubMethodNames.IssueCreated,
            payload =>
            {
                _logger.LogDebug("IssueCreated: {Rule} in {File}:{Line}",
                    payload.RuleId, payload.FilePath, payload.StartLine);
                DispatchEvent(IssueCreated, new IssueCreatedEventArgs { Payload = payload });
            });

        // IssueClosed — issue auto-stängt
        conn.On<IssueClosedEvent>(
            HubMethodNames.IssueClosed,
            payload =>
            {
                _logger.LogDebug("IssueClosed: {Id} Reason={Reason}",
                    payload.IssueId, payload.Reason);
                DispatchEvent(IssueClosed, new IssueClosedEventArgs { Payload = payload });
            });

        // HealthScoreUpdated
        conn.On<HealthScoreUpdatedEvent>(
            HubMethodNames.HealthScoreUpdated,
            payload =>
            {
                _logger.LogInformation("HealthScore: {Old:F1} → {New:F1}",
                    payload.OldScore, payload.NewScore);
                DispatchEvent(HealthScoreUpdated,
                    new HealthScoreUpdatedEventArgs { Payload = payload });
            });

        // Reconnected-event från HubConnection
        conn.Reconnected += async connectionId =>
        {
            _reconnectAttempt = 0;
            SetState(ConnectionStateSnapshot.Connected());
            _logger.LogInformation("SignalR reconnected. ConnectionId={Id}", connectionId);

            // Återgå med i org-grupp
            var orgId = GetOrgIdFromToken();
            if (orgId is not null)
                await conn.InvokeAsync(HubMethodNames.JoinOrgGroup, orgId.Value.ToString("D"));
        };

        conn.Reconnecting += ex =>
        {
            _reconnectAttempt++;
            SetState(ConnectionStateSnapshot.Reconnecting(
                _reconnectAttempt,
                GetReconnectDelay(_reconnectAttempt),
                ex?.Message));
            return Task.CompletedTask;
        };
    }

    private static async Task WaitForDisconnectAsync(HubConnection conn, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.Closed += ex =>
        {
            tcs.TrySetResult(true);
            return Task.CompletedTask;
        };

        using (ct.Register(() => tcs.TrySetCanceled()))
            await tcs.Task;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private void SetState(ConnectionStateSnapshot newState)
    {
        _currentState = newState;
        DispatchEvent(ConnectionStateChanged, newState);
    }

    private void DispatchEvent<T>(EventHandler<T>? handler, T args) where T : class
    {
        if (handler is null) return;

        // Kör alltid på UI-tråden
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(() => handler.Invoke(this, args));
        else
            handler.Invoke(this, args);
    }

    private string BuildHubUrl() =>
        _auth.ApiBaseUrl.TrimEnd('/') + "/hubs/analysis";

    private Guid? GetOrgIdFromToken()
    {
        var token = _auth.GetCachedToken();
        if (token is null) return null;
        return JwtOrgIdExtractor.Extract(token);
    }

    private static bool IsAuthError(Exception ex) =>
        ex.Message.Contains("401") ||
        ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        (ex.InnerException?.Message.Contains("401") == true);

    private static TimeSpan GetReconnectDelay(int attempt)
    {
        var index = Math.Min(attempt - 1, ReconnectDelays.Length - 1);
        return ReconnectDelays[index];
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

// ═══════════════════════════════════════════════════════════════════════════
// CustomReconnectPolicy  — styr automatisk reconnect-timing
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Ger HubConnection:s inbyggda AutomaticReconnect samma exponentiella
/// backoff-mönster som vår manuella logik.
/// </summary>
internal sealed class CustomReconnectPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(0),   // Direkt försök
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < Delays.Length
            ? Delays[retryContext.PreviousRetryCount]
            : null; // null = ge upp → ConnectWithRetryAsync tar över
}

// ═══════════════════════════════════════════════════════════════════════════
// JwtOrgIdExtractor  — läs OrgId från token utan extern JWT-bibliotek
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Extraherar <c>synthtax:org_id</c>-claimet direkt från JWT-payloaden
/// utan att validera signaturen — vi litar på att tokenet är giltigt
/// (redan validerat av API-servern vid inloggning).
/// </summary>
internal static class JwtOrgIdExtractor
{
    public static Guid? Extract(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            // Base64url → Base64
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Synthtax-specifikt claim: "synthtax:org_id"
            if (doc.RootElement.TryGetProperty("synthtax:org_id", out var orgIdEl))
            {
                var raw = orgIdEl.GetString();
                return Guid.TryParse(raw, out var id) ? id : null;
            }
            return null;
        }
        catch { return null; }
    }
}
