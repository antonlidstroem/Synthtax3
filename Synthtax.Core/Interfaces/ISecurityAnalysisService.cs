using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface ISecurityAnalysisService
{
    /// <summary>
    /// Kör full säkerhetsanalys på en solution.
    /// </summary>
    Task<SecurityAnalysisResultDto> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter hårdkodade credentials (lösenord, API-nycklar, connection strings).
    /// </summary>
    Task<List<SecurityIssueDto>> FindHardcodedCredentialsAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter potentiella SQL-injection-risker.
    /// </summary>
    Task<List<SecurityIssueDto>> FindSqlInjectionRisksAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter osäkra Random-anrop (ska använda RandomNumberGenerator).
    /// </summary>
    Task<List<SecurityIssueDto>> FindInsecureRandomUsageAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter async-metoder som saknar CancellationToken-parameter.
    /// </summary>
    Task<List<SecurityIssueDto>> FindMissingCancellationTokensAsync(string solutionPath, CancellationToken cancellationToken = default);
}
