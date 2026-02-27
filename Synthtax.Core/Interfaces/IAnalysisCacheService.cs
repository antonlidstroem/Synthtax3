using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Interfaces;

public interface IAnalysisCacheService
{
    /// <summary>
    /// Creates a new analysis session and returns its ID.
    /// </summary>
    Task<Guid> CreateSessionAsync(
        string solutionPath,
        string sessionType,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a batch of issues to the session.
    /// </summary>
    Task SaveIssuesAsync(
        Guid sessionId,
        IEnumerable<SavedIssueDto> issues,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a session by ID.
    /// </summary>
    Task<AnalysisSessionDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all non-expired sessions, optionally filtered by solution path.
    /// </summary>
    Task<List<AnalysisSessionDto>> ListSessionsAsync(
        string? solutionPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves issues for a session with pagination and optional filtering.
    /// </summary>
    Task<SessionIssuesResultDto> GetIssuesAsync(
        Guid sessionId,
        int page = 1,
        int pageSize = 50,
        string? issueType = null,
        Severity? severity = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes expired sessions and their issues.
    /// </summary>
    Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific session and all its issues.
    /// </summary>
    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
