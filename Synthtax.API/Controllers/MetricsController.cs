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
public class MetricsController : ControllerBase
{
    private readonly IMetricsService _metricsService;
    private readonly RepositoryResolverService _resolver;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        IMetricsService metricsService,
        RepositoryResolverService resolver,
        ILogger<MetricsController> logger)
    {
        _metricsService = metricsService;
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Beräknar metrics för en hel solution.
    /// Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL.
    /// </summary>
    [HttpPost("solution")]
    [ProducesResponseType(typeof(MetricsResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeSolution(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        _logger.LogInformation("Metrics analysis: {Path}", resolved.LocalPath);
        try
        {
            var result = await _metricsService.AnalyzeSolutionMetricsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Beräknar metrics för ett enskilt projekt.</summary>
    [HttpPost("project")]
    [ProducesResponseType(typeof(MetricsResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeProject(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectPath))
            return BadRequest(new { Message = "ProjectPath is required." });

        var result = await _metricsService.AnalyzeProjectMetricsAsync(
            request.ProjectPath, cancellationToken);
        return Ok(result);
    }

    /// <summary>Hämtar trenddata (LOC, komplexitet, MI).</summary>
    [HttpGet("trend")]
    [ProducesResponseType(typeof(List<MetricsTrendPointDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrend(
        [FromQuery] string solutionPath,
        [FromQuery] int maxDataPoints = 30,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _metricsService.GetMetricsTrendAsync(
                resolved.LocalPath!, maxDataPoints, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Beräknar metrics för en enskild fil.</summary>
    [HttpGet("file")]
    [ProducesResponseType(typeof(FileMetricsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeFile(
        [FromQuery] string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return BadRequest(new { Message = "filePath is required." });

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { Message = $"File not found: {filePath}" });

        var result = await _metricsService.AnalyzeFileMetricsAsync(filePath, cancellationToken);
        return Ok(result);
    }
}
