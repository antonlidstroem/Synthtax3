using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
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
    private readonly RepositoryResolverService _resolver;

    public SecurityController(
        ISecurityAnalysisService securityService,
        RepositoryResolverService resolver)
    {
        _securityService = securityService;
        _resolver = resolver;
    }

    /// <summary>
    /// Kör fullständig säkerhetsanalys.
    /// Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL.
    /// </summary>
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

    /// <summary>Söker efter hårdkodade credentials.</summary>
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

    /// <summary>Söker efter SQL-injection-risker.</summary>
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

    /// <summary>Söker efter osäkert användande av Random.</summary>
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

    /// <summary>Söker efter saknade CancellationTokens.</summary>
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
}
