using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class SecurityIssueDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string? CodeSnippet { get; set; }
    public Severity Severity { get; set; }
    public string Category { get; set; } = string.Empty;
}

public class SecurityAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public int TotalIssues { get; set; }
    public int CriticalCount { get; set; }
    public int HighCount { get; set; }
    public int MediumCount { get; set; }
    public int LowCount { get; set; }
    public List<SecurityIssueDto> HardcodedCredentials { get; set; } = new();
    public List<SecurityIssueDto> SqlInjectionRisks { get; set; } = new();
    public List<SecurityIssueDto> InsecureRandomUsage { get; set; } = new();
    public List<SecurityIssueDto> MissingCancellationTokens { get; set; } = new();
    public List<SecurityIssueDto> AllIssues { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
