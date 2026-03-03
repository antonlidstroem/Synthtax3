using Synthtax.Core.Events;

namespace Synthtax.Core.Interfaces;

/// <summary>
/// Bas-interface for att skicka realtidsnotifikationer till anslutna klienter.
/// Implementeras i API-lagret med SignalR.
/// </summary>
public interface IHubPusher
{
    Task PushAnalysisUpdatedAsync(AnalysisUpdatedEvent payload,       CancellationToken ct = default);
    Task PushIssueCreatedAsync(IssueCreatedEvent payload,             CancellationToken ct = default);
    Task PushIssueClosedAsync(IssueClosedEvent payload,               CancellationToken ct = default);
    Task PushIssueStatusChangedAsync(IssueStatusChangedEvent payload, CancellationToken ct = default);
    Task PushHealthScoreUpdatedAsync(HealthScoreUpdatedEvent payload, CancellationToken ct = default);
}
