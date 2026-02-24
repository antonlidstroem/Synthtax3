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
public class AIDetectionController : ControllerBase
{
    private readonly IAIDetectionService _aiDetectionService;
    private readonly RepositoryResolverService _resolver;

    public AIDetectionController(
        IAIDetectionService aiDetectionService,
        RepositoryResolverService resolver)
    {
        _aiDetectionService = aiDetectionService;
        _resolver = resolver;
    }

    /// <summary>
    /// Analyserar en hel solution för AI-genererade kodmönster.
    /// Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL.
    /// </summary>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(AIDetectionResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeSolution(
        [FromBody] AnalysisRequestDto request,
        CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _aiDetectionService.AnalyzeSolutionAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Analyserar en enskild fil.</summary>
    [HttpGet("file")]
    [ProducesResponseType(typeof(AIDetectionFileResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeFile(
        [FromQuery] string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return BadRequest(new { Message = "filePath is required." });

        if (!System.IO.File.Exists(filePath))
            return NotFound(new { Message = $"File not found: {filePath}" });

        var result = await _aiDetectionService.AnalyzeFileAsync(filePath, cancellationToken);
        return Ok(result);
    }

    /// <summary>Analyserar kod skickad direkt som sträng.</summary>
    [HttpPost("code")]
    [ProducesResponseType(typeof(AIDetectionFileResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeCode(
        [FromBody] AnalyzeCodeRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return BadRequest(new { Message = "Code is required." });

        var result = await _aiDetectionService.AnalyzeCodeAsync(
            request.Code, request.FileName ?? "inline.cs", cancellationToken);
        return Ok(result);
    }


}
