using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class CodeIssueDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public int LineCount { get; set; }
    public string? MethodName { get; set; }
    public string? Snippet { get; set; }
    public Severity Severity { get; set; }
}

public class CodeAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public int TotalIssues { get; set; }

    // Namngivna listor för de tre inbyggda reglerna
    public List<CodeIssueDto> LongMethods { get; set; } = new();
    public List<CodeIssueDto> DeadVariables { get; set; } = new();
    public List<CodeIssueDto> UnnecessaryUsings { get; set; } = new();

    // BUG-03 FIX: Uppsamlingslista för alla framtida regler (CA004, CA005 …).
    // Tidigare föll issues från okända regeltyper på golvet (ingen default-case).
    public List<CodeIssueDto> AllOtherIssues { get; set; } = new();

    public List<string> Errors { get; set; } = new();
}

public class AnalysisRequestDto
{
    public string? SolutionPath { get; set; }
    public string? ProjectPath { get; set; }
}
