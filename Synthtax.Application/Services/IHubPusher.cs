using Synthtax.Core.Events;
using Synthtax.Realtime.Contracts;

namespace Synthtax.Application.Services;

/// <summary>
/// Applikationslagrets port mot realtidskommunikation.
/// Implementeras i Synthtax.Backend (SynthtaxHubPusher).
/// </summary>
public interface IHubPusher
{
    Task PushAnalysisUpdatedAsync(AnalysisUpdatedEvent payload,
        CancellationToken ct = default);

    Task PushIssueStatusChangedAsync(IssueStatusChangedEvent payload,
        CancellationToken ct = default);

   
}
