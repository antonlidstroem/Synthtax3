using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class CodeFixDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the suggested fix.
    /// </summary>
    public string SuggestedFix { get; set; } = string.Empty;

    /// <summary>
    /// Concrete code snippet showing the fix applied.
    /// </summary>
    public string FixedCodeSnippet { get; set; } = string.Empty;

    /// <summary>
    /// The original code snippet that has the issue.
    /// </summary>
    public string OriginalCodeSnippet { get; set; } = string.Empty;

    public Severity Priority { get; set; } = Severity.Critical;

    /// <summary>
    /// Whether this fix can be applied automatically (safe transforms only).
    /// </summary>
    public bool IsAutoFixable { get; set; }
}
