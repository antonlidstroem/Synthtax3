using Synthtax.Core.Enums;

namespace Synthtax.Infrastructure.Entities;

public class SavedAnalysisIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public AnalysisSession Session { get; set; } = null!;

    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public int EndLineNumber { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The raw source lines involved in the issue (max ~50 lines stored).
    /// </summary>
    public string CodeSnippet { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable fix description.
    /// </summary>
    public string SuggestedFix { get; set; } = string.Empty;

    /// <summary>
    /// Concrete code showing what the corrected code looks like.
    /// </summary>
    public string FixedCodeSnippet { get; set; } = string.Empty;

    public string? MethodName { get; set; }
    public string? ClassName { get; set; }
    public bool IsAutoFixable { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
