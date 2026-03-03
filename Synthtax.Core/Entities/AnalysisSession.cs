namespace Synthtax.Core.Entities;

public class AnalysisSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; } // Tillagd
    public string SolutionPath { get; set; } = string.Empty;
    public string SessionType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime Timestamp => CreatedAt; // Alias för bakåtkompatibilitet
    public DateTime ExpiresAt { get; set; }
    public TimeSpan ScanDuration { get; set; } // Tillagd
    public double OverallScore { get; set; } // Tillagd
    public int TotalIssues { get; set; }
    public List<string> Errors { get; set; } = new(); // Tillagd

    public ICollection<SavedAnalysisIssue> Issues { get; set; } = new List<SavedAnalysisIssue>();
}