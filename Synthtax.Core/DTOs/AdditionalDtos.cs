namespace Synthtax.Core.DTOs;

// ────────────────────────────────────────────────────────────────────────────
// BUG-01 FIX: FullAnalysisOptions har tagits bort härifrån.
//   Den kanoniska definitionen finns i:
//   Synthtax.Core/Interfaces/ISolutionAnalysisPipeLine.cs
//
// BUG-02 FIX: PipelineRequestDto har tagits bort härifrån.
//   Den kanoniska definitionen finns i:
//   Synthtax.Core/DTOs/AdditionalDtos.cs (denna fil, se nedan)
// ────────────────────────────────────────────────────────────────────────────

public class IssueSummaryDto
{
    public Guid SessionId { get; set; }
    public string SessionType { get; set; } = string.Empty;
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int TotalIssues { get; set; }
    public int AutoFixableCount { get; set; }
    public int FilesAffected { get; set; }
    public Dictionary<string, int> BySeverity { get; set; } = new();
    public Dictionary<string, int> ByIssueType { get; set; } = new();
}

public class IssueSearchResultDto
{
    public List<SavedIssueDto> Issues { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
    public string? Query { get; set; }
    public string? IssueTypeFilter { get; set; }
}

public class UpdateStatusDto
{
    public Synthtax.Core.Enums.BacklogStatus Status { get; set; }
}

public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
}

public class SetActiveDto
{
    public bool IsActive { get; set; }
}

public class UpdateRoleDto
{
    public string Role { get; set; } = "User";
}

/// <summary>
/// BUG-02 FIX: Kanonisk PipelineRequestDto — borttagen inline-klass i PipelineController.
/// </summary>
public class PipelineRequestDto
{
    public string? SolutionPath { get; set; }

    /// <summary>
    /// Använder den kanoniska FullAnalysisOptions från ISolutionAnalysisPipeLine.cs.
    /// </summary>
    public Synthtax.Core.Interfaces.FullAnalysisOptions? Options { get; set; }

}
