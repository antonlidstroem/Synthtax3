using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Interfaces;

/// <summary>
/// Cache-tjänst för analyssessioner och sparade issues (SQLite-backed).
/// </summary>
public interface IAnalysisCacheService
{
    // ── Session management ────────────────────────────────────────────────────
    Task<Guid> CreateSessionAsync(
        string solutionPath,
        string sessionType,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    Task<AnalysisSessionDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<List<AnalysisSessionDto>> ListSessionsAsync(
        string? solutionPath = null,
        CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default);

    // ── Issue storage ─────────────────────────────────────────────────────────
    Task SaveIssuesAsync(
        Guid sessionId,
        IEnumerable<SavedIssueDto> issues,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar issues paginerat med valfria filter.
    /// </summary>
    Task<SessionIssuesResultDto> GetIssuesAsync(
        Guid sessionId,
        int page = 1,
        int pageSize = 50,
        string? issueType = null,
        Severity? severity = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar en enskild issue inklusive fullständig kodsekvens.
    /// Returnerar null om issue inte finns eller session har löpt ut.
    /// </summary>
    Task<SavedIssueDto?> GetIssueByIdAsync(
        Guid issueId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker bland alla icke-utgångna issues oavsett session.
    /// Stöder fritext i Description/CodeSnippet samt filter på issueType och severity.
    /// </summary>
    Task<IssueSearchResultDto> SearchIssuesAsync(
        string? query = null,
        string? issueType = null,
        Severity? severity = null,
        bool? autoFixableOnly = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Beräknar aggregerad statistik för en session med SQL GROUP BY – laddar INTE issues i RAM.
    /// </summary>
    Task<IssueSummaryDto> GetSessionSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
