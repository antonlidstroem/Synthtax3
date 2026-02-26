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
    private readonly ICodeAnalysisService _codeAnalysisService;
    private readonly RepositoryResolverService _resolver;
    private readonly ILogger<CodeAnalysisController> _logger;

    public CodeAnalysisController(
        ICodeAnalysisService codeAnalysisService,
        RepositoryResolverService resolver,
        ILogger<CodeAnalysisController> logger)
    {
        _codeAnalysisService = codeAnalysisService;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Analyserar en hel solution. Accepterar lokal .sln-sökväg, lokal mapp
    /// eller en GitHub-URL (https://github.com/user/repo).
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
            var result = await _codeAnalysisService.AnalyzeSolutionAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Analyserar ett enskilt projekt (.csproj).
    /// </summary>
    [HttpPost("project")]
    [ProducesResponseType(typeof(CodeAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeProject(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return BadRequest(new { Message = "ProjectPath saknas." });

        if (!System.IO.File.Exists(request.ProjectPath))
            return NotFound(new { Message = $"Projektfilen hittades inte: {request.ProjectPath}" });

        var result = await _codeAnalysisService.AnalyzeProjectAsync(
            request.ProjectPath, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Hittar långa metoder. Accepterar lokal .sln-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("long-methods")]
    [ProducesResponseType(typeof(List<CodeIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindLongMethods(
        [FromQuery] string solutionPath,
        [FromQuery] int maxLines = 50,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _codeAnalysisService.FindLongMethodsAsync(
                resolved.LocalPath!, maxLines, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hittar oanvända (döda) variabler. Accepterar lokal .sln-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("dead-variables")]
    [ProducesResponseType(typeof(List<CodeIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindDeadVariables(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _codeAnalysisService.FindDeadVariablesAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hittar onödiga using-direktiv. Accepterar lokal .sln-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("unnecessary-usings")]
    [ProducesResponseType(typeof(List<CodeIssueDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FindUnnecessaryUsings(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _codeAnalysisService.FindUnnecessaryUsingsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
