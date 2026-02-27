namespace Synthtax.Infrastructure.Entities;

public class AnalysisSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SolutionPath { get; set; } = string.Empty;

    /// <summary>
    /// E.g. "SemanticDeadVariable", "SemanticSqlInjection", "CognitiveComplexity", "MissingCancellationToken"
    /// </summary>
    public string SessionType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public int TotalIssues { get; set; }

    public ICollection<SavedAnalysisIssue> Issues { get; set; } = new List<SavedAnalysisIssue>();
}
