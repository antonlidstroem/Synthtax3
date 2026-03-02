namespace Synthtax.Vsix.Client;

// ═══════════════════════════════════════════════════════════════════════════
// API-svar DTO:er  — speglar Synthtax API:s JSON-kontrakt
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Ärende hämtat från Synthtax API.</summary>
public sealed class BacklogItemDto
{
    public Guid   Id              { get; init; }
    public string RuleId          { get; init; } = "";
    public string RuleName        { get; init; } = "";
    public string Category        { get; init; } = "";
    public string Severity        { get; init; } = "Medium";  // "Low"|"Medium"|"High"|"Critical"
    public string Status          { get; init; } = "Open";
    public string FilePath        { get; init; } = "";
    public int    StartLine       { get; init; }
    public string Snippet         { get; init; } = "";
    public string Message         { get; init; } = "";
    public string? Suggestion     { get; init; }
    public string? FixedSnippet   { get; init; }
    public bool   IsAutoFixable   { get; init; }
    public string? ClassName      { get; init; }
    public string? MemberName     { get; init; }
    public string? Namespace      { get; init; }
    public string? ProjectName    { get; init; }
    public DateTime CreatedAt     { get; init; }
    public DateTime? LastSeenAt   { get; init; }

    // ── Hjälpegenskaper ───────────────────────────────────────────────────

    public bool IsCritical  => Severity is "Critical";
    public bool IsHigh      => Severity is "High";
    public bool IsOpen      => Status is "Open" or "Acknowledged" or "InProgress";
}

/// <summary>Projekthälsa — summering för Tool Window-headern.</summary>
public sealed class ProjectHealthDto
{
    public string ProjectName    { get; init; } = "";
    public double OverallScore   { get; init; }   // 0–100
    public int    TotalIssues    { get; init; }
    public int    CriticalCount  { get; init; }
    public int    HighCount      { get; init; }
    public int    MediumCount    { get; init; }
    public int    LowCount       { get; init; }
    public DateTime? LastAnalyzed { get; init; }
    public string SubscriptionPlan { get; init; } = "Free";
}

/// <summary>Svar vid lyckad inloggning.</summary>
public sealed class AuthResponseDto
{
    public string AccessToken  { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public DateTime ExpiresAt  { get; init; }
    public string UserName     { get; init; } = "";
    public string OrgSlug      { get; init; } = "";
}

/// <summary>Svar vid generering av AI-prompt via API.</summary>
public sealed class PromptResponseDto
{
    public string Content     { get; init; } = "";
    public string Target      { get; init; } = "Copilot";
    public string Title       { get; init; } = "";
    public int EstimatedTokens { get; init; }
}

/// <summary>Paginerat svar från /api/v1/backlog.</summary>
public sealed class PagedBacklogDto
{
    public IReadOnlyList<BacklogItemDto> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page       { get; init; }
    public int PageSize   { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(PageSize, 1));
}
