using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

/// <summary>
/// Query and manage saved analysis sessions and their cached issues.
/// Sessions are temporary (default TTL: 24 hours).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class AnalysisResultsController : ControllerBase
{
    private readonly IAnalysisCacheService _cache;
    private readonly ILogger<AnalysisResultsController> _logger;

    public AnalysisResultsController(
        IAnalysisCacheService cache,
        ILogger<AnalysisResultsController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// List all active (non-expired) analysis sessions, optionally filtered by solution path.
    /// </summary>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<AnalysisSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSessions(
        [FromQuery] string? solutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _cache.ListSessionsAsync(solutionPath, cancellationToken);
        return Ok(sessions);
    }

    /// <summary>
    /// Get metadata for a specific session.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(AnalysisSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _cache.GetSessionAsync(sessionId, cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    /// <summary>
    /// Get all issues for a session with pagination and optional filtering.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/issues")]
    [ProducesResponseType(typeof(SessionIssuesResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIssues(
        Guid sessionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? issueType = null,
        [FromQuery] Severity? severity = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var session = await _cache.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
            return NotFound(new { Message = $"Session {sessionId} not found or expired." });

        var result = await _cache.GetIssuesAsync(
            sessionId, page, pageSize, issueType, severity, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get a summary of issue counts grouped by type for a session.
    /// </summary>
    [HttpGet("sessions/{sessionId:guid}/summary")]
    [ProducesResponseType(typeof(IssueSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessionSummary(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _cache.GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
            return NotFound(new { Message = $"Session {sessionId} not found or expired." });

        // Fetch all issues (up to 10k) to compute summary
        var all = await _cache.GetIssuesAsync(
            sessionId, page: 1, pageSize: 10_000, cancellationToken: cancellationToken);

        var summary = new IssueSummaryDto
        {
            SessionId = sessionId,
            SessionType = session.SessionType,
            TotalIssues = all.TotalCount,
            BySeverity = all.Issues
                .GroupBy(i => i.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            ByIssueType = all.Issues
                .GroupBy(i => i.IssueType)
                .ToDictionary(g => g.Key, g => g.Count()),
            AutoFixableCount = all.Issues.Count(i => i.IsAutoFixable),
            FilesAffected = all.Issues.Select(i => i.FilePath).Distinct().Count()
        };

        return Ok(summary);
    }

    /// <summary>
    /// Delete a specific session and all its issues.
    /// </summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _cache.DeleteSessionAsync(sessionId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Manually trigger cleanup of all expired sessions.
    /// </summary>
    [HttpPost("sessions/cleanup")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cleanup(CancellationToken cancellationToken = default)
    {
        var count = await _cache.CleanupExpiredSessionsAsync(cancellationToken);
        _logger.LogInformation("Manual cleanup removed {Count} expired sessions", count);
        return Ok(new { RemovedSessions = count });
    }
}

public class IssueSummaryDto
{
    public Guid SessionId { get; set; }
    public string SessionType { get; set; } = string.Empty;
    public int TotalIssues { get; set; }
    public int AutoFixableCount { get; set; }
    public int FilesAffected { get; set; }
    public Dictionary<string, int> BySeverity { get; set; } = new();
    public Dictionary<string, int> ByIssueType { get; set; } = new();
}
