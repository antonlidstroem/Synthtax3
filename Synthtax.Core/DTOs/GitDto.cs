namespace Synthtax.Core.DTOs;

public class GitCommitDto
{
    public string Sha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public DateTime AuthoredAt { get; set; }
    public DateTime CommittedAt { get; set; }
    public int FilesChanged { get; set; }
    public int Insertions { get; set; }
    public int Deletions { get; set; }
    public string BranchName { get; set; } = string.Empty;
}

public class GitBranchDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsRemote { get; set; }
    public bool IsCurrentBranch { get; set; }
    public string? TrackingBranch { get; set; }
    public string TipSha { get; set; } = string.Empty;
    public DateTime LastCommitDate { get; set; }
    public string LastCommitAuthor { get; set; } = string.Empty;
    public string LastCommitMessage { get; set; } = string.Empty;
}

public class GitChurnDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int CommitCount { get; set; }
    public int TotalInsertions { get; set; }
    public int TotalDeletions { get; set; }
    public int TotalChurn { get; set; }
    public DateTime FirstChanged { get; set; }
    public DateTime LastChanged { get; set; }
    public List<string> Authors { get; set; } = new();
}

public class BusFactorDto
{
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public int CommitCount { get; set; }
    public double Percentage { get; set; }
    public List<string> PrimaryFiles { get; set; } = new();
}

public class GitAnalysisResultDto
{
    public string RepositoryPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public string CurrentBranch { get; set; } = string.Empty;
    public int TotalCommits { get; set; }
    public int TotalBranches { get; set; }
    public int TotalContributors { get; set; }
    public List<GitCommitDto> RecentCommits { get; set; } = new();
    public List<GitBranchDto> Branches { get; set; } = new();
    public List<GitChurnDto> FileChurn { get; set; } = new();
    public List<BusFactorDto> BusFactor { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class GitRequestDto
{
    public string RepositoryPath { get; set; } = string.Empty;
    public int MaxCommits { get; set; } = 100;
}
