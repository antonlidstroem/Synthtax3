using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting; // Nytt
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")] // Versionering tillagd för konsekvens
[Authorize(Policy = "UserOrAdmin")] // Använder policyn vi skapade i Program.cs
[EnableRateLimiting("analysis")] // Aktiverar rate limiting för alla endpoints
[Produces("application/json")]
public class AnalysisResultsController : SynthtaxControllerBase
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

    // ── Sessions ─────────────────────────────────────────────────────────────

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<AnalysisSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSessions(
        [FromQuery] string? solutionPath = null,
        CancellationToken ct = default)
    {
        // Tips: Här kan du i framtiden filtrera på User.GetOrganizationId() 
        // om cachade sessioner ska vara isolerade per org.
        var sessions = await _cache.ListSessionsAsync(solutionPath, ct);
        return Ok(sessions);
    }

    [HttpGet("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(AnalysisSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(Guid sessionId, CancellationToken ct)
    {
        var session = await _cache.GetSessionAsync(sessionId, ct);
        return session is null ? SessionNotFound(sessionId) : Ok(session);
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    [Authorize(Policy = "OrgAdminOrSystemAdmin")] // Bara admins får radera sessioner
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        _logger.LogWarning("User {User} is deleting session {SessionId}", User.Identity?.Name, sessionId);
        await _cache.DeleteSessionAsync(sessionId, ct);
        return NoContent();
    }

    [HttpPost("sessions/cleanup")]
    [Authorize(Policy = "OrgAdminOrSystemAdmin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cleanup(CancellationToken ct)
    {
        var count = await _cache.CleanupExpiredSessionsAsync(ct);
        _logger.LogInformation("Manual cleanup removed {Count} expired sessions", count);
        return Ok(new { RemovedSessions = count, Timestamp = DateTime.UtcNow });
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
        CancellationToken ct = default)
    {
        ValidatePagination(ref page, ref pageSize);

        var session = await _cache.GetSessionAsync(sessionId, ct);
        if (session is null) return SessionNotFound(sessionId);

        var result = await _cache.GetIssuesAsync(sessionId, page, pageSize, issueType, severity, ct);
        return Ok(result);
    }

    [HttpGet("issues/{issueId:guid}")]
    [ProducesResponseType(typeof(SavedIssueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIssueById(Guid issueId, CancellationToken ct)
    {
        var issue = await _cache.GetIssueByIdAsync(issueId, ct);
        if (issue is null)
            return NotFound(new ProblemDetails { Title = "Issue Not Found", Detail = $"Issue {issueId} not found or expired." });

        return Ok(issue);
    }

    [HttpGet("issues/search")]
    [ProducesResponseType(typeof(IssueSearchResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchIssues(
        [FromQuery] string? q = null,
        [FromQuery] string? issueType = null,
        [FromQuery] Severity? severity = null,
        [FromQuery] bool? autoFixableOnly = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        ValidatePagination(ref page, ref pageSize);

        var result = await _cache.SearchIssuesAsync(q, issueType, severity, autoFixableOnly, page, pageSize, ct);
        return Ok(result);
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    [HttpGet("sessions/{sessionId:guid}/summary")]
    [ProducesResponseType(typeof(IssueSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessionSummary(Guid sessionId, CancellationToken ct)
    {
        var session = await _cache.GetSessionAsync(sessionId, ct);
        if (session is null) return SessionNotFound(sessionId);

        var summary = await _cache.GetSessionSummaryAsync(sessionId, ct);
        return Ok(summary);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ValidatePagination(ref int page, ref int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize switch
        {
            < 1 => 50,
            > 200 => 200,
            _ => pageSize
        };
    }

    private NotFoundObjectResult SessionNotFound(Guid id) =>
        NotFound(new ProblemDetails
        {
            Status = 404,
            Title = "Session Not Found",
            Detail = $"Analysis session {id} has expired or never existed."
        });
}