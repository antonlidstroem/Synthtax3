using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IGitAnalysisService
{
    /// <summary>
    /// Kör full Git-analys: commits, brancher, churn, bus factor.
    /// </summary>
    Task<GitAnalysisResultDto> AnalyzeRepositoryAsync(string repositoryPath, int maxCommits = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar senaste commits.
    /// </summary>
    Task<List<GitCommitDto>> GetCommitsAsync(string repositoryPath, int maxCommits = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar alla brancher i repot.
    /// </summary>
    Task<List<GitBranchDto>> GetBranchesAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Beräknar fil-churn (hur ofta filer ändrats).
    /// </summary>
    Task<List<GitChurnDto>> GetFileChurnAsync(string repositoryPath, int maxCommits = 200, CancellationToken cancellationToken = default);

    /// <summary>
    /// Beräknar bus factor (kunskapskoncentration per författare).
    /// </summary>
    Task<List<BusFactorDto>> GetBusFactorAsync(string repositoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Kontrollerar om en sökväg är ett giltigt Git-repo.
    /// </summary>
    bool IsValidRepository(string path);
}
