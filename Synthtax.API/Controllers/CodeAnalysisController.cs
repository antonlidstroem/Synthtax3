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
public class CodeAnalysisController : ControllerBase
{
    private readonly ICodeAnalysisService _analysisService;
    private readonly SemanticCodeAnalysisService _semanticService;
    private readonly RepositoryResolverService _resolver;
    private readonly ILogger<CodeAnalysisController> _logger;

    public CodeAnalysisController(
        ICodeAnalysisService analysisService,
        SemanticCodeAnalysisService semanticService,
        RepositoryResolverService resolver,
        ILogger<CodeAnalysisController> logger)
    {
        _analysisService = analysisService;
        _semanticService = semanticService;
        _resolver = resolver;
        _logger = logger;
    }

    // ── Existing endpoints (unchanged) ────────────────────────────────────────

    [HttpPost("solution")]
    [ProducesResponseType(typeof(CodeAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeSolution(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        _logger.LogInformation("Code analysis: {Path}", resolved.LocalPath);
        try
        {
            var result = await _analysisService.AnalyzeSolutionAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpPost("project")]
    [ProducesResponseType(typeof(CodeAnalysisResultDto), StatusCodes.Status200OK)]
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

    // ── NEW: Semantic endpoints ────────────────────────────────────────────────

    /// <summary>
    /// DataFlow-based dead variable detection. More accurate than syntactic analysis.
    /// Results are saved to the analysis cache and accessible via /api/analysisresults.
    /// Returns a session ID for retrieving paginated results.
    /// </summary>
    [HttpGet("dead-variables/semantic")]
    [ProducesResponseType(typeof(SemanticAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FindDeadVariablesSemantic(
        [FromQuery] string solutionPath,
        [FromQuery] bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _semanticService.FindDeadVariablesSemanticAsync(
                resolved.LocalPath!, saveToCache, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>
    /// Cognitive Complexity analysis following the SonarSource specification.
    /// Reports methods exceeding the threshold with concrete refactoring advice.
    /// </summary>
    [HttpGet("cognitive-complexity")]
    [ProducesResponseType(typeof(SemanticAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FindHighCognitiveComplexity(
        [FromQuery] string solutionPath,
        [FromQuery] int threshold = 15,
        [FromQuery] bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _semanticService.FindHighCognitiveComplexityAsync(
                resolved.LocalPath!, threshold, saveToCache, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>
    /// Async hygiene analysis: async void, .Result/.Wait() on Tasks.
    /// </summary>
    [HttpGet("async-hygiene")]
    [ProducesResponseType(typeof(SemanticAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FindAsyncHygieneIssues(
        [FromQuery] string solutionPath,
        [FromQuery] bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try
        {
            var result = await _semanticService.FindAsyncHygieneIssuesAsync(
                resolved.LocalPath!, saveToCache, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }
}
