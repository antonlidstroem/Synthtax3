using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Synthtax.Realtime.Contracts;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.Client;

/// <summary>
/// Konkret implementation av ISynthtaxHubClient.
/// Hanterar: anslutning, återanslutning, org-grupper och inkommande events.
/// BUGFIX: Klassen saknades helt — ISynthtaxHubClient var ett interface utan implementation.
/// </summary>
public sealed class SynthtaxHubClient : ISynthtaxHubClient
{
    private readonly string                      _hubUrl;
    private readonly ILogger<SynthtaxHubClient>  _logger;
    private          HubConnection?              _connection;
    private          HubConnectionState          _state = HubConnectionState.Disconnected;
    private readonly SemaphoreSlim               _connectLock = new(1, 1);

    public HubConnectionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            _state = value;
            ConnectionStateChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<AnalysisUpdatedEvent>?    AnalysisUpdated;
    public event EventHandler<IssueCreatedEvent>?       IssueCreated;
    public event EventHandler<IssueClosedEvent>?        IssueClosed;
    public event EventHandler<IssueStatusChangedEvent>? IssueStatusChanged;
    public event EventHandler<LicenseChangedEvent>?     LicenseChanged;
    public event EventHandler<HeartbeatEvent>?          Heartbeat;
    public event EventHandler<HubConnectionState>?      ConnectionStateChanged;

    public SynthtaxHubClient(string hubUrl, ILogger<SynthtaxHubClient> logger)
    {
        _hubUrl = hubUrl;
        _logger = logger;
    }

    public async Task StartAsync(string accessToken, CancellationToken ct = default)
    {
        await _connectLock.WaitAsync(ct);
        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            _connection = new HubConnectionBuilder()
                .WithUrl(_hubUrl, opts =>
                {
                    opts.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                })
                .WithAutomaticReconnect(new ReconnectPolicy())
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
                })
                .Build();

            RegisterHandlers(_connection);
            WireLifecycleEvents(_connection);

            State = HubConnectionState.Connecting;
            try
            {
                await _connection.StartAsync(ct);
                State = HubConnectionState.Connected;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("SignalR-anslutning nekad — token ogiltig.");
                State = HubConnectionState.AuthError;
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_connection is null) return;
        await _connection.StopAsync(ct);
        State = HubConnectionState.Disconnected;
    }

    public Task JoinOrgGroupAsync(string organizationId, CancellationToken ct = default)
        => _connection?.InvokeAsync(HubMethodNames.JoinOrgGroup, organizationId, ct)
           ?? Task.CompletedTask;

    public Task LeaveOrgGroupAsync(string organizationId, CancellationToken ct = default)
        => _connection?.InvokeAsync(HubMethodNames.LeaveOrgGroup, organizationId, ct)
           ?? Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        _connectLock.Dispose();
    }

    // ─── Handlers ─────────────────────────────────────────────────────────────

    private void RegisterHandlers(HubConnection conn)
    {
        conn.On<AnalysisUpdatedEvent>(
            HubMethodNames.AnalysisUpdated,
            e => AnalysisUpdated?.Invoke(this, e));

        conn.On<IssueCreatedEvent>(
            HubMethodNames.IssueCreated,
            e => IssueCreated?.Invoke(this, e));

        conn.On<IssueClosedEvent>(
            HubMethodNames.IssueClosed,
            e => IssueClosed?.Invoke(this, e));

        conn.On<IssueStatusChangedEvent>(
            HubMethodNames.IssueStatusChanged,
            e => IssueStatusChanged?.Invoke(this, e));

        conn.On<LicenseChangedEvent>(
            HubMethodNames.LicenseChanged,
            e => LicenseChanged?.Invoke(this, e));

        conn.On<HeartbeatEvent>(
            HubMethodNames.Heartbeat,
            e => Heartbeat?.Invoke(this, e));
    }

    private void WireLifecycleEvents(HubConnection conn)
    {
        conn.Reconnecting += ex =>
        {
            _logger.LogDebug(ex, "SignalR återansluter…");
            State = HubConnectionState.Reconnecting;
            return Task.CompletedTask;
        };

        conn.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR återansluten. ConnId={Id}", connectionId);
            State = HubConnectionState.Connected;
            return Task.CompletedTask;
        };

        conn.Closed += ex =>
        {
            if (ex is not null)
                _logger.LogWarning(ex, "SignalR-anslutning stängd med fel.");
            else
                _logger.LogDebug("SignalR-anslutning stängd.");

            State = HubConnectionState.Disconnected;
            return Task.CompletedTask;
        };
    }

    // ─── Återanslutningspolicy ────────────────────────────────────────────────

    private sealed class ReconnectPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
        [
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
        ];

        public TimeSpan? NextRetryDelay(RetryContext ctx)
        {
            var idx = (int)Math.Min(ctx.PreviousRetryCount, Delays.Length - 1);
            return Delays[idx];
        }
    }
}
