namespace Synthtax.Realtime.Contracts;

/// <summary>Kanonisk lista över alla SignalR-metodnamn.</summary>
public static class HubMethodNames
{
    // Server → Klient
    public const string AnalysisUpdated    = "AnalysisUpdated";
    public const string IssueCreated       = "IssueCreated";
    public const string IssueClosed        = "IssueClosed";
    public const string IssueStatusChanged = "IssueStatusChanged";
    public const string HealthScoreUpdated = "HealthScoreUpdated";
    public const string LicenseChanged     = "LicenseChanged";
    public const string Heartbeat          = "Heartbeat";

    // Klient → Server
    public const string JoinOrgGroup         = "JoinOrgGroup";
    public const string LeaveOrgGroup        = "LeaveOrgGroup";
    public const string AcknowledgeHeartbeat = "AcknowledgeHeartbeat";
}

// ─── Server → Klient events ──────────────────────────────────────────────────

public sealed record AnalysisUpdatedEvent
{
    public required Guid                          OrganizationId   { get; init; }
    public required Guid                          ProjectId        { get; init; }
    public required string                        ProjectName      { get; init; }
    public required double                        HealthScore      { get; init; }
    public required int                           TotalIssues      { get; init; }
    public required int                           NewIssueCount    { get; init; }
    public required int                           ClosedIssueCount { get; init; }
    public required DateTime                      AnalyzedAt       { get; init; }
    public          Guid                          SessionId        { get; init; }
    public          IReadOnlyList<HubBacklogItem> Issues           { get; init; } = [];
    public          IReadOnlyList<Guid>           ClosedIssueIds   { get; init; } = [];
    public          bool HasChanges => NewIssueCount > 0 || ClosedIssueCount > 0;
}

public sealed record IssueCreatedEvent
{
    public required Guid    OrganizationId { get; init; }
    public required Guid    IssueId        { get; init; }
    public required string  RuleId         { get; init; }
    public required string  Severity       { get; init; }
    public required string  FilePath       { get; init; }
    public required int     StartLine      { get; init; }
    public required string  Message        { get; init; }
    public          string? ClassName      { get; init; }
    public          string? MemberName     { get; init; }
}

public sealed record IssueClosedEvent
{
    public required Guid   OrganizationId { get; init; }
    public required Guid   IssueId        { get; init; }
    public required string RuleId         { get; init; }
    public required string FilePath       { get; init; }
    /// <summary>"AutoClosed" | "Resolved" | "FalsePositive"</summary>
    public required string Reason         { get; init; }
}

public sealed record IssueStatusChangedEvent
{
    public required Guid     OrganizationId { get; init; }
    public required Guid     IssueId        { get; init; }
    public required string   OldStatus      { get; init; }
    public required string   NewStatus      { get; init; }
    public required string   ChangedByUser  { get; init; }
    public required DateTime ChangedAt      { get; init; }
}

public sealed record HealthScoreUpdatedEvent
{
    public required Guid     OrganizationId { get; init; }
    public required Guid     ProjectId      { get; init; }
    public required double   OldScore       { get; init; }
    public required double   NewScore       { get; init; }
    public required int      TotalIssues    { get; init; }
    public required int      CriticalCount  { get; init; }
    public required int      HighCount      { get; init; }
    public required DateTime ChangedAt      { get; init; }
    public          double   Delta          => NewScore - OldScore;
}

public sealed record LicenseChangedEvent
{
    public required Guid   OrganizationId { get; init; }
    public required string OldPlan        { get; init; }
    public required string NewPlan        { get; init; }
    public required string Message        { get; init; }
}

public sealed record HeartbeatEvent
{
    public DateTime ServerTime       { get; init; } = DateTime.UtcNow;
    public int      ConnectedClients { get; init; }
}

// ─── Delad modell ─────────────────────────────────────────────────────────────

public sealed record HubBacklogItem
{
    public required Guid    Id            { get; init; }
    public required string  RuleId        { get; init; }
    public required string  Title         { get; init; }
    public required string  Severity      { get; init; }
    public required string  Status        { get; init; }
    public required string  FilePath      { get; init; }
    public required int     StartLine     { get; init; }
    public required string  Message       { get; init; }
    public          string? ClassName     { get; init; }
    public          string? MemberName    { get; init; }
    public          string? Namespace     { get; init; }
    public          bool    IsAutoFixable { get; init; }
    public          string? Snippet       { get; init; }
    public          string? Suggestion    { get; init; }
}
