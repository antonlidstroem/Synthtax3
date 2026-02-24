namespace Synthtax.Core.DTOs;

public class PullRequestDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string TargetBranch { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Open, Merged, Closed
    public DateTime CreatedAt { get; set; }
    public DateTime? MergedAt { get; set; }
    public int CommentsCount { get; set; }
    public int FilesChanged { get; set; }
    public int Insertions { get; set; }
    public int Deletions { get; set; }
    public List<string> Reviewers { get; set; } = new();
    public List<string> Labels { get; set; } = new();
}
