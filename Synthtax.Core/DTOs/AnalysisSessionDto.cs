using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class AnalysisSessionDto
{
    public Guid Id { get; set; }
    public string SolutionPath { get; set; } = string.Empty;
    public string SessionType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int TotalIssues { get; set; }
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}

public class SavedIssueDto
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public int EndLineNumber { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The actual lines of source code where the issue was found.
    /// </summary>
    public string CodeSnippet { get; set; } = string.Empty;

    /// <summary>
    /// Concrete suggested fix with example code.
    /// </summary>
    public string SuggestedFix { get; set; } = string.Empty;

    /// <summary>
    /// Concrete code showing what the fix looks like.
    /// </summary>
    public string FixedCodeSnippet { get; set; } = string.Empty;

    public string? MethodName { get; set; }
    public string? ClassName { get; set; }
    public bool IsAutoFixable { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SessionIssuesResultDto
{
    public AnalysisSessionDto Session { get; set; } = new();
    public List<SavedIssueDto> Issues { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}
