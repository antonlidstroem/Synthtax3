using Synthtax.Realtime.Contracts;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.Client;

public interface ISynthtaxHubClient : IAsyncDisposable
{
    HubConnectionState State { get; }

    event EventHandler<AnalysisUpdatedEvent>?    AnalysisUpdated;
    event EventHandler<IssueCreatedEvent>?       IssueCreated;
    event EventHandler<IssueClosedEvent>?        IssueClosed;
    event EventHandler<IssueStatusChangedEvent>? IssueStatusChanged;
    event EventHandler<LicenseChangedEvent>?     LicenseChanged;
    event EventHandler<HeartbeatEvent>?          Heartbeat;
    event EventHandler<HubConnectionState>?      ConnectionStateChanged;

    Task StartAsync(string accessToken, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task JoinOrgGroupAsync(string organizationId, CancellationToken ct = default);
    Task LeaveOrgGroupAsync(string organizationId, CancellationToken ct = default);
}
