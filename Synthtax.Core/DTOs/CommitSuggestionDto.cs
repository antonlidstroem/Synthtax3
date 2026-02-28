namespace Synthtax.Core.DTOs;

public class CommitSuggestionDto
{
    /// <summary>Suggested subject line in Conventional Commits format, e.g. "feat(css): add unused selector detection".</summary>
    public string  Subject     { get; set; } = string.Empty;

    /// <summary>Optional multi-line body listing changed files / areas.</summary>
    public string  Body        { get; set; } = string.Empty;

    /// <summary>Full message = Subject + blank line + Body (when Body is non-empty).</summary>
    public string  FullMessage => string.IsNullOrWhiteSpace(Body)
        ? Subject
        : $"{Subject}\n\n{Body}";

    /// <summary>Detected type: feat | fix | refactor | style | docs | test | chore | build | ci | perf.</summary>
    public string  Type        { get; set; } = string.Empty;

    /// <summary>Optional scope derived from the most-changed directory, e.g. "auth" or "css".</summary>
    public string? Scope       { get; set; }

    /// <summary>0–1. Higher means the heuristics matched more strongly.</summary>
    public double  Confidence  { get; set; }

    /// <summary>Alternative suggestions ranked by descending confidence.</summary>
    public List<CommitSuggestionDto> Alternatives { get; set; } = new();

    // Change statistics
    public int FilesChanged { get; set; }
    public int Insertions   { get; set; }
    public int Deletions    { get; set; }

    /// <summary>Per-file change breakdown, used to build the body and in the UI.</summary>
    public List<CommitChangeSummaryDto> ChangeSummary { get; set; } = new();

    public List<string> Errors { get; set; } = new();
}

public class CommitChangeSummaryDto
{
    public string  FilePath   { get; set; } = string.Empty;
    public string  FileName   { get; set; } = string.Empty;
    /// <summary>Added | Modified | Deleted | Renamed | Copied.</summary>
    public string  ChangeType { get; set; } = string.Empty;
    public int     Insertions { get; set; }
    public int     Deletions  { get; set; }
    public string? OldPath    { get; set; }
}
