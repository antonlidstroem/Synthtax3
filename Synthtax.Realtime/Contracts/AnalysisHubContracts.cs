namespace Synthtax.Realtime.Contracts;

// ═══════════════════════════════════════════════════════════════════════════
// Hub-metodnamn  — strängar som delas av server och VSIX-klient
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Konstanter för SignalR-metodnamn.
///
/// <para>Namnkonvention: PascalCase, speglar server-metoden.
/// Klient-handlers registreras med samma sträng via
/// <c>HubConnection.On(HubMethodNames.AnalysisUpdated, …)</c>.</para>
/// </summary>
public static class HubMethodNames
{
    // ── Server → Klient (push-event) ───────────────────────────────────────
    /// <summary>Analyssession slutförd — hela backloggen har uppdaterats.</summary>
    public const string AnalysisUpdated      = "AnalysisUpdated";

    /// <summary>Ett enskilt nytt issue har skapats.</summary>
    public const string IssueCreated         = "IssueCreated";

    /// <summary>Ett enskilt issue har stängts/lösts automatiskt.</summary>
    public const string IssueClosed          = "IssueClosed";

    /// <summary>Projekthälsopoängen har förändrats (ny scan eller manual-fix).</summary>
    public const string HealthScoreUpdated   = "HealthScoreUpdated";

    /// <summary>En plan-/licensändring har skett i org:en.</summary>
    public const string LicenseChanged       = "LicenseChanged";

    // ── Klient → Server (joins) ────────────────────────────────────────────
    /// <summary>Klienten prenumererar på en organisations events.</summary>
    public const string JoinOrgGroup         = "JoinOrgGroup";

    /// <summary>Klienten lämnar en organisations event-grupp.</summary>
    public const string LeaveOrgGroup        = "LeaveOrgGroup";
}

// ═══════════════════════════════════════════════════════════════════════════
// Event-payload DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Payload för <see cref="HubMethodNames.AnalysisUpdated"/>.
/// Skickas när en komplett analyssession är klar.
/// </summary>
public sealed record AnalysisUpdatedEvent
{
    /// <summary>Organisation/tenant som körde analysen.</summary>
    public required Guid   OrganizationId  { get; init; }

    /// <summary>Projekt som analyserades.</summary>
    public required Guid   ProjectId       { get; init; }

    /// <summary>Projektnamn för display i VSIX.</summary>
    public required string ProjectName     { get; init; }

    /// <summary>Ny hälsopoäng (0–100).</summary>
    public required double HealthScore     { get; init; }

    /// <summary>Totalt antal öppna issues efter sessionen.</summary>
    public required int    TotalIssues     { get; init; }

    /// <summary>Antal nya issues i denna session.</summary>
    public required int    NewIssueCount   { get; init; }

    /// <summary>Antal auto-stängda issues i denna session.</summary>
    public required int    ClosedIssueCount { get; init; }

    /// <summary>Tidpunkt för analysen (UTC).</summary>
    public required DateTime AnalyzedAt    { get; init; }

    /// <summary>
    /// Inkluderade issues (paginerat; max 50 per event).
    /// Tom om <see cref="TotalIssues"/> är noll.
    /// </summary>
    public IReadOnlyList<HubBacklogItem> Issues { get; init; } = [];

    public bool HasChanges => NewIssueCount > 0 || ClosedIssueCount > 0;
}

/// <summary>
/// Payload för <see cref="HubMethodNames.IssueCreated"/>.
/// Skickas per-issue för inkrementell update vid stora kodbaser.
/// </summary>
public sealed record IssueCreatedEvent
{
    public required Guid   OrganizationId { get; init; }
    public required Guid   IssueId        { get; init; }
    public required string RuleId         { get; init; }
    public required string Severity       { get; init; }
    public required string FilePath       { get; init; }
    public required int    StartLine      { get; init; }
    public required string Message        { get; init; }
    public          string? ClassName     { get; init; }
    public          string? MemberName    { get; init; }
}

/// <summary>
/// Payload för <see cref="HubMethodNames.IssueClosed"/>.
/// </summary>
public sealed record IssueClosedEvent
{
    public required Guid   OrganizationId { get; init; }
    public required Guid   IssueId        { get; init; }
    public required string RuleId         { get; init; }
    public required string FilePath       { get; init; }
    public required string Reason         { get; init; } // "AutoClosed" | "Resolved" | "FalsePositive"
}

/// <summary>
/// Payload för <see cref="HubMethodNames.HealthScoreUpdated"/>.
/// </summary>
public sealed record HealthScoreUpdatedEvent
{
    public required Guid   OrganizationId { get; init; }
    public required Guid   ProjectId      { get; init; }
    public required double OldScore       { get; init; }
    public required double NewScore       { get; init; }
    public required int    TotalIssues    { get; init; }
    public required int    CriticalCount  { get; init; }
    public required int    HighCount      { get; init; }
    public required DateTime ChangedAt    { get; init; }
    public double Delta => NewScore - OldScore;
}

/// <summary>
/// Kompakt issue-representation inuti <see cref="AnalysisUpdatedEvent"/>.
/// Speglar <c>BacklogItemDto</c> men är optimerad för hub-transport.
/// </summary>
public sealed record HubBacklogItem
{
    public required Guid   Id          { get; init; }
    public required string RuleId      { get; init; }
    public required string Severity    { get; init; }
    public required string Status      { get; init; }
    public required string FilePath    { get; init; }
    public required int    StartLine   { get; init; }
    public required string Message     { get; init; }
    public          string? ClassName  { get; init; }
    public          string? MemberName { get; init; }
    public          bool IsAutoFixable { get; init; }
    public          string? Snippet    { get; init; }
    public          string? Suggestion { get; init; }
}
