namespace Synthtax.Core.DTOs;

/// <summary>
/// Result from semantic (data-flow aware) analysis passes.
/// Returned by SemanticCodeAnalysisService and SemanticSecurityAnalysisService.
/// </summary>
public class SemanticAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public string AnalysisType { get; set; } = string.Empty;

    /// <summary>Set by the API layer after persisting to cache (not set by the Analysis library).</summary>
    public Guid? SessionId { get; set; }

    public int TotalIssues { get; set; }
    public List<SavedIssueDto> Issues { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
