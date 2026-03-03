using Synthtax.Realtime.Contracts;

namespace Synthtax.Application.Orchestration;

public sealed record OrchestratorResult
{
    public double                        OverallScore    { get; init; }
    public int                           NewIssues       { get; init; }
    public int                           ResolvedIssues  { get; init; }
    public int                           TotalIssues     { get; init; }
    public TimeSpan                      TotalDuration   { get; init; }
    public IReadOnlyList<HubBacklogItem> NewItemsSummary { get; init; } = [];
    public IReadOnlyList<Guid>           ClosedIssueIds  { get; init; } = [];
}
