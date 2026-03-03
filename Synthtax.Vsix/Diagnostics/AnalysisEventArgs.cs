using Synthtax.Realtime.Contracts;

namespace Synthtax.Vsix.Diagnostics;

public sealed class AnalysisUpdatedEventArgs : EventArgs
{
    public required AnalysisUpdatedEvent Payload { get; init; }
}

public sealed class IssueCreatedEventArgs : EventArgs
{
    public required IssueCreatedEvent Payload { get; init; }
}

public sealed class IssueClosedEventArgs : EventArgs
{
    public required IssueClosedEvent Payload { get; init; }
}

public sealed class IssueStatusChangedEventArgs : EventArgs
{
    public required IssueStatusChangedEvent Payload { get; init; }
}
