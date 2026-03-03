using Synthtax.Core.Enums;

namespace Synthtax.Core.Entities;

public class SavedAnalysisIssue
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty; // Tillagd
    public int LineNumber { get; set; } // Tillagd
    public int StartLine => LineNumber;
    public int EndLineNumber { get; set; } // Tillagd
    public string IssueType { get; set; } = string.Empty; // Tillagd
    public Severity Severity { get; set; }
    public string Description { get; set; } = string.Empty; // Tillagd
    public string Message { get; set; } = string.Empty;
    public string? CodeSnippet { get; set; } // Tillagd
    public string? SuggestedFix { get; set; } // Tillagd
    public string? FixedCodeSnippet { get; set; } // Tillagd
    public string? MethodName { get; set; } // Tillagd
    public string? ClassName { get; set; } // Tillagd
    public bool IsAutoFixable { get; set; } // Tillagd
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Tillagd

    public AnalysisSession? Session { get; set; }
}