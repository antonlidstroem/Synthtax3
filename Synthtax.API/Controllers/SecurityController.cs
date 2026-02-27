using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
using Synthtax.API.Services.Analysis;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class SecurityController : ControllerBase
{
    private readonly ISecurityAnalysisService _securityService;
    private readonly SemanticSecurityAnalysisService _semanticSecurityService;
    private readonly RepositoryResolverService _resolver;

    public SecurityController(
        ISecurityAnalysisService securityService,
        SemanticSecurityAnalysisService semanticSecurityService,
        RepositoryResolverService resolver)
    {
        _securityService = securityService;
        _semanticSecurityService = semanticSecurityService;
        _resolver = resolver;
    }

    // ── Existing endpoints (unchanged) ────────────────────────────────────────

    [HttpPost("analyze")]
    [ProducesResponseType(typeof(SecurityAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _securityService.AnalyzeSolutionAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("credentials")]
    [ProducesResponseType(typeof(List<SecurityIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindCredentials(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _securityService.FindHardcodedCredentialsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("sql-injection")]
    [ProducesResponseType(typeof(List<SecurityIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindSqlInjection(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _securityService.FindSqlInjectionRisksAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("insecure-random")]
    [ProducesResponseType(typeof(List<SecurityIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindInsecureRandom(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _securityService.FindInsecureRandomUsageAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("cancellation-tokens")]
    [ProducesResponseType(typeof(List<SecurityIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindMissingCancellationTokens(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _securityService.FindMissingCancellationTokensAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    // ── NEW: Semantic endpoints ────────────────────────────────────────────────

    /// <summary>
    /// Symbol-level SQL injection detection.
    /// Verifies types via IMethodSymbol – far fewer false positives than syntactic analysis.
    /// Results are saved to the cache with concrete fix suggestions.
    /// </summary>
    [HttpGet("sql-injection/semantic")]
    [ProducesResponseType(typeof(SemanticAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FindSqlInjectionSemantic(
        [FromQuery] string solutionPath,
        [FromQuery] bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _semanticSecurityService.FindSqlInjectionRisksSemanticAsync(
                resolved.LocalPath!, saveToCache, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>
    /// Contract-aware missing CancellationToken detection.
    /// Skips overrides, interface implementations, and event handlers.
    /// Includes concrete fix suggestions and propagation advice.
    /// </summary>
    [HttpGet("cancellation-tokens/semantic")]
    [ProducesResponseType(typeof(SemanticAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FindMissingCancellationTokensSemantic(
        [FromQuery] string solutionPath,
        [FromQuery] bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _semanticSecurityService.FindMissingCancellationTokensSemanticAsync(
                resolved.LocalPath!, saveToCache, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }
}
