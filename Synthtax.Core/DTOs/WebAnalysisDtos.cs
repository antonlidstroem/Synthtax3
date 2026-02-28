using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

// ── Single issue found by a language plugin ────────────────────────────────────

public class WebIssueDto
{
    public string   FilePath       { get; set; } = string.Empty;
    public string   FileName       { get; set; } = string.Empty;
    public string   Language       { get; set; } = string.Empty;   // "CSS" | "JavaScript" | "HTML"
    public string   RuleId         { get; set; } = string.Empty;
    public string   IssueType      { get; set; } = string.Empty;
    public string   Title          { get; set; } = string.Empty;
    public string   Description    { get; set; } = string.Empty;
    public string?  Recommendation { get; set; }
    public int      LineNumber     { get; set; }
    public int      EndLine        { get; set; }
    public string?  CodeSnippet    { get; set; }
    public Severity Severity       { get; set; }
    public string   Category       { get; set; } = string.Empty;
    public bool     IsAutoFixable  { get; set; }
    public string?  FixedCode      { get; set; }
}

// ── Per-file result ────────────────────────────────────────────────────────────

public class WebFileResultDto
{
    public string   FilePath   { get; set; } = string.Empty;
    public string   FileName   { get; set; } = string.Empty;
    public string   Language   { get; set; } = string.Empty;
    public int      IssueCount { get; set; }
    public List<WebIssueDto> Issues { get; set; } = new();
    public List<string>      Errors { get; set; } = new();
}

// ── Full directory/project result ─────────────────────────────────────────────

public class WebAnalysisResultDto
{
    public string   ProjectPath  { get; set; } = string.Empty;
    public DateTime AnalyzedAt   { get; set; } = DateTime.UtcNow;
    public int      FilesAnalyzed { get; set; }
    public int      TotalIssues  { get; set; }
    public int      CriticalCount { get; set; }
    public int      HighCount    { get; set; }
    public int      MediumCount  { get; set; }
    public int      LowCount     { get; set; }

    /// <summary>Issues grouped by language name.</summary>
    public Dictionary<string, List<WebIssueDto>> ByLanguage { get; set; } = new();

    public List<WebFileResultDto> FileResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

// ── Plugin discovery ───────────────────────────────────────────────────────────

public class LanguagePluginInfoDto
{
    public string Language    { get; set; } = string.Empty;
    public string Version     { get; set; } = string.Empty;
    public List<string> SupportedExtensions { get; set; } = new();
    public List<PluginRuleInfoDto> Rules     { get; set; } = new();
}

public class PluginRuleInfoDto
{
    public string   RuleId      { get; set; } = string.Empty;
    public string   Name        { get; set; } = string.Empty;
    public string   Description { get; set; } = string.Empty;
    public Severity DefaultSeverity { get; set; }
    public bool     IsEnabled   { get; set; }
}
