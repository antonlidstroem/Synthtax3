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
public class CodeAnalysisController : ControllerBase
{
    private readonly ICodeAnalysisService _analysisService;
    private readonly RepositoryResolverService _resolver;
    private readonly ILogger<CodeAnalysisController> _logger;

    public CodeAnalysisController(
        ICodeAnalysisService analysisService,
        RepositoryResolverService resolver,
        ILogger<CodeAnalysisController> logger)
    {
        _analysisService = analysisService;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Analyserar en hel solution med Roslyn.
    /// Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL i SolutionPath.
    /// </summary>
    [HttpPost("solution")]
    [ProducesResponseType(typeof(CodeAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeSolution(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        _logger.LogInformation("Code analysis: {Path}", resolved.LocalPath);

        try
        {
            var result = await _analysisService.AnalyzeSolutionAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Analyserar ett enskilt projekt (.csproj).</summary>
    [HttpPost("project")]
    [ProducesResponseType(typeof(CodeAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeProject(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return BadRequest(new { Message = "ProjectPath is required." });

        var result = await _analysisService.AnalyzeProjectAsync(
            request.ProjectPath, cancellationToken);
        return Ok(result);
    }

    /// <summary>Söker efter långa metoder.</summary>
    [HttpGet("long-methods")]
    [ProducesResponseType(typeof(List<CodeIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindLongMethods(
        [FromQuery] string solutionPath,
        [FromQuery] int threshold = 100,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _analysisService.FindLongMethodsAsync(
                resolved.LocalPath!, threshold, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Söker efter oanvända variabler.</summary>
    [HttpGet("dead-variables")]
    [ProducesResponseType(typeof(List<CodeIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindDeadVariables(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _analysisService.FindDeadVariablesAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Söker efter onödiga using-satser.</summary>
    [HttpGet("unnecessary-usings")]
    [ProducesResponseType(typeof(List<CodeIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindUnnecessaryUsings(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _analysisService.FindUnnecessaryUsingsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }
}
