namespace Synthtax.Core.DTOs;

/// <summary>
/// Aggregerad statistik för en analyssession.
/// Beräknas via SQL GROUP BY – laddar inte alla issues i minnet.
/// Tidigare definierad inuti AnalysisResultsController.cs.
/// </summary>
public class IssueSummaryDto
{
    public Guid SessionId { get; set; }
    public string SessionType { get; set; } = string.Empty;
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int TotalIssues { get; set; }
    public int AutoFixableCount { get; set; }
    public int FilesAffected { get; set; }

    /// <summary>Antal issues per allvarlighetsnivå, t.ex. { "Critical": 2, "High": 7 }</summary>
    public Dictionary<string, int> BySeverity { get; set; } = new();

    /// <summary>Antal issues per typ, t.ex. { "DeadVariable": 15, "SqlInjection": 3 }</summary>
    public Dictionary<string, int> ByIssueType { get; set; } = new();
}

/// <summary>
/// Resultat av tvärsnittssökning bland alla sparade issues oavsett session.
/// </summary>
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

/// <summary>
/// Enkel DTO för PATCH-status på backlog-items.
/// Tidigare definierad inuti BacklogController.cs.
/// </summary>
public class UpdateStatusDto
{
    public Synthtax.Core.Enums.BacklogStatus Status { get; set; }
}

/// <summary>
/// DTO för att uppdatera profil.
/// Tidigare definierad inuti UsersController.cs.
/// </summary>
public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
}

/// <summary>
/// DTO för att sätta en användares aktiva-status.
/// Tidigare definierad inuti AdminController.cs.
/// </summary>
public class SetActiveDto
{
    public bool IsActive { get; set; }
}

/// <summary>
/// DTO för att uppdatera en användares roll.
/// Tidigare definierad inuti AdminController.cs.
/// </summary>
public class UpdateRoleDto
{
    public string Role { get; set; } = "User";
}

/// <summary>
/// Request-DTO för pipeline-analysen.
/// Tidigare definierad inuti PipelineController.cs.
/// </summary>
public class PipelineRequestDto
{
    public string? SolutionPath { get; set; }
    public FullAnalysisOptions? Options { get; set; }
}

/// <summary>
/// Alternativ för fullständig pipeline-analys.
/// </summary>
public class FullAnalysisOptions
{
    public bool IncludeCode { get; set; } = true;
    public bool IncludeSecurity { get; set; } = true;
    public bool IncludeMetrics { get; set; } = true;
    public bool IncludeCoupling { get; set; } = true;
    public bool IncludeAIDetection { get; set; } = false;
}
