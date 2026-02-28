using LibGit2Sharp;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class GitAnalysisService : IGitAnalysisService
{
    private readonly ILogger<GitAnalysisService> _logger;

    public GitAnalysisService(ILogger<GitAnalysisService> logger) => _logger = logger;

    public async Task<GitAnalysisResultDto> AnalyzeRepositoryAsync(
        string repositoryPath, int maxCommits = 100, CancellationToken cancellationToken = default)
    {
        var result = new GitAnalysisResultDto { RepositoryPath = repositoryPath };
        try
        {
            await Task.Run(() =>
            {
                using var repo = new Repository(repositoryPath);
                result.CurrentBranch      = repo.Head.FriendlyName;
                result.RecentCommits      = GetCommitsInternal(repo, maxCommits);
                result.Branches           = GetBranchesInternal(repo);
                result.FileChurn          = GetFileChurnInternal(repo, Math.Min(maxCommits * 2, 500));
                result.BusFactor          = GetBusFactorInternal(repo, result.RecentCommits);
                result.TotalCommits       = result.RecentCommits.Count;
                result.TotalBranches      = result.Branches.Count;
                result.TotalContributors  = result.RecentCommits
                    .Select(c => c.AuthorEmail).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (RepositoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Not a valid Git repository: {Path}", repositoryPath);
            result.Errors.Add($"Not a valid Git repository: {repositoryPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing Git repository {Path}", repositoryPath);
            result.Errors.Add($"Git analysis error: {ex.Message}");
        }
        return result;
    }

    public async Task<List<GitCommitDto>> GetCommitsAsync(
        string repositoryPath, int maxCommits = 100, CancellationToken cancellationToken = default)
        => await Task.Run(() => { using var repo = new Repository(repositoryPath); return GetCommitsInternal(repo, maxCommits); }, cancellationToken);

    public async Task<List<GitBranchDto>> GetBranchesAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
        => await Task.Run(() => { using var repo = new Repository(repositoryPath); return GetBranchesInternal(repo); }, cancellationToken);

    public async Task<List<GitChurnDto>> GetFileChurnAsync(
        string repositoryPath, int maxCommits = 200, CancellationToken cancellationToken = default)
        => await Task.Run(() => { using var repo = new Repository(repositoryPath); return GetFileChurnInternal(repo, maxCommits); }, cancellationToken);

    public async Task<List<BusFactorDto>> GetBusFactorAsync(
        string repositoryPath, CancellationToken cancellationToken = default)
        => await Task.Run(() =>
        {
            using var repo = new Repository(repositoryPath);
            var commits = GetCommitsInternal(repo, 500);
            return GetBusFactorInternal(repo, commits);
        }, cancellationToken);

    public bool IsValidRepository(string path)
    {
        try { return Repository.IsValid(path); } catch { return false; }
    }

    private static List<GitCommitDto> GetCommitsInternal(Repository repo, int maxCommits)
    {
        var filter = new CommitFilter { SortBy = CommitSortStrategies.Time, IncludeReachableFrom = repo.Head };
        return repo.Commits.QueryBy(filter).Take(maxCommits).Select(c =>
        {
            var stats = GetCommitStats(repo, c);
            return new GitCommitDto
            {
                Sha = c.Sha, ShortSha = c.Sha[..Math.Min(7, c.Sha.Length)],
                Message = c.MessageShort.Trim(), AuthorName = c.Author.Name,
                AuthorEmail = c.Author.Email, AuthoredAt = c.Author.When.UtcDateTime,
                CommittedAt = c.Committer.When.UtcDateTime,
                FilesChanged = stats.filesChanged, Insertions = stats.insertions,
                Deletions = stats.deletions, BranchName = repo.Head.FriendlyName
            };
        }).ToList();
    }

    private static (int filesChanged, int insertions, int deletions) GetCommitStats(Repository repo, Commit commit)
    {
        try
        {
            if (!commit.Parents.Any()) return (0, 0, 0);
            var patch = repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree);
            return (patch.Count(), patch.Sum(p => p.LinesAdded), patch.Sum(p => p.LinesDeleted));
        }
        catch { return (0, 0, 0); }
    }

    private static List<GitBranchDto> GetBranchesInternal(Repository repo)
        => repo.Branches.OrderBy(b => b.IsRemote).ThenBy(b => b.FriendlyName).Select(b =>
        {
            var tip = b.Tip;
            return new GitBranchDto
            {
                Name = b.FriendlyName, IsRemote = b.IsRemote,
                IsCurrentBranch = b.IsCurrentRepositoryHead,
                TrackingBranch = b.TrackedBranch?.FriendlyName,
                TipSha = tip?.Sha[..Math.Min(7, tip.Sha.Length)] ?? string.Empty,
                LastCommitDate = tip?.Author.When.UtcDateTime ?? DateTime.MinValue,
                LastCommitAuthor = tip?.Author.Name ?? string.Empty,
                LastCommitMessage = tip?.MessageShort.Trim() ?? string.Empty
            };
        }).ToList();

    private static List<GitChurnDto> GetFileChurnInternal(Repository repo, int maxCommits)
    {
        var fileStats = new Dictionary<string, GitChurnDto>(StringComparer.OrdinalIgnoreCase);
        var filter    = new CommitFilter { SortBy = CommitSortStrategies.Time, IncludeReachableFrom = repo.Head };

        foreach (var commit in repo.Commits.QueryBy(filter).Take(maxCommits))
        {
            if (!commit.Parents.Any()) continue;
            try
            {
                var patch = repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree);
                foreach (var entry in patch)
                {
                    var path = entry.Path;
                    if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!fileStats.TryGetValue(path, out var churn))
                    {
                        churn = new GitChurnDto
                        {
                            FilePath = path, FileName = Path.GetFileName(path),
                            FirstChanged = commit.Author.When.UtcDateTime, LastChanged = commit.Author.When.UtcDateTime
                        };
                        fileStats[path] = churn;
                    }
                    churn.CommitCount++; churn.TotalInsertions += entry.LinesAdded;
                    churn.TotalDeletions += entry.LinesDeleted; churn.TotalChurn += entry.LinesAdded + entry.LinesDeleted;
                    if (commit.Author.When.UtcDateTime < churn.FirstChanged) churn.FirstChanged = commit.Author.When.UtcDateTime;
                    if (commit.Author.When.UtcDateTime > churn.LastChanged)  churn.LastChanged  = commit.Author.When.UtcDateTime;
                    if (!churn.Authors.Contains(commit.Author.Name)) churn.Authors.Add(commit.Author.Name);
                }
            }
            catch { }
        }
        return fileStats.Values.OrderByDescending(f => f.CommitCount).ToList();
    }

    private static List<BusFactorDto> GetBusFactorInternal(Repository repo, List<GitCommitDto> commits)
    {
        var authorCommits = commits
            .GroupBy(c => c.AuthorEmail, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Email = g.Key, Name = g.First().AuthorName, Count = g.Count() })
            .OrderByDescending(a => a.Count).ToList();

        var total = commits.Count;
        if (total == 0) return new List<BusFactorDto>();

        var filter     = new CommitFilter { SortBy = CommitSortStrategies.Time, IncludeReachableFrom = repo.Head };
        var authorFiles = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var commit in repo.Commits.QueryBy(filter).Take(200))
        {
            if (!commit.Parents.Any()) continue;
            try
            {
                var patch = repo.Diff.Compare<Patch>(commit.Parents.First().Tree, commit.Tree);
                if (!authorFiles.ContainsKey(commit.Author.Email))
                    authorFiles[commit.Author.Email] = new HashSet<string>();
                foreach (var entry in patch) authorFiles[commit.Author.Email].Add(entry.Path);
            }
            catch { }
        }

        return authorCommits.Select(a => new BusFactorDto
        {
            AuthorName = a.Name, AuthorEmail = a.Email, CommitCount = a.Count,
            Percentage  = Math.Round((double)a.Count / total * 100, 1),
            PrimaryFiles = authorFiles.TryGetValue(a.Email, out var files) ? files.Take(10).ToList() : new List<string>()
        }).ToList();
    }
}
