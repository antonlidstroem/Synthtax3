using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class AnalysisResultsController : SynthtaxControllerBase
{
    private readonly IAnalysisCacheService _cache;
    private readonly ILogger<AnalysisResultsController> _logger;

    public AnalysisResultsController(
        IAnalysisCacheService cache,
        ILogger<AnalysisResultsController> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    // ── Sessions ─────────────────────────────────────────────────────────────

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<AnalysisSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSessions(
        [FromQuery] string? solutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _cache.ListSessionsAsync(solutionPath, cancellationToken);
        return Ok(sessions);
    }

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

    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _cache.DeleteSessionAsync(sessionId, cancellationToken);
        return NoContent();
    }

    [HttpPost("sessions/cleanup")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cleanup(CancellationToken cancellationToken = default)
    {
        var count = await _cache.CleanupExpiredSessionsAsync(cancellationToken);
        _logger.LogInformation("Manual cleanup removed {Count} expired sessions", count);
        return Ok(new { RemovedSessions = count });
    }

    // ── Issues ────────────────────────────────────────────────────────────────

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
    /// Hämtar en enskild sparad issue inklusive fullständig kodsekvens (CodeSnippet, FixedCodeSnippet).
    /// </summary>
    [HttpGet("issues/{issueId:guid}")]
    [ProducesResponseType(typeof(SavedIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIssueById(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        var issue = await _cache.GetIssueByIdAsync(issueId, cancellationToken);
        if (issue is null)
            return NotFound(new { Message = $"Issue {issueId} not found or session expired." });
        return Ok(issue);
    }

    /// <summary>
    /// Söker bland alla sparade issues oavsett session.
    /// Stöder fritext i beskrivning/kodsekvens, filter på issueType och severity.
    /// </summary>
    [HttpGet("issues/search")]
    [ProducesResponseType(typeof(IssueSearchResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchIssues(
        [FromQuery] string? q = null,
        [FromQuery] string? issueType = null,
        [FromQuery] Severity? severity = null,
        [FromQuery] bool? autoFixableOnly = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var result = await _cache.SearchIssuesAsync(
            q, issueType, severity, autoFixableOnly, page, pageSize, cancellationToken);
        return Ok(result);
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returnerar aggregerad statistik för en session beräknad med SQL GROUP BY.
    /// Tidigare laddades upp till 10 000 issues i minnet för LINQ-beräkning.
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

        var summary = await _cache.GetSessionSummaryAsync(sessionId, cancellationToken);
        return Ok(summary);
    }
}
