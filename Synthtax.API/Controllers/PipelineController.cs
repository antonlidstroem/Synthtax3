using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
using Synthtax.Core.DTOs;       // PipelineRequestDto finns nu bara här (BUG-02 fix)
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class PipelineController : ControllerBase
{
    private readonly ISolutionAnalysisPipeline _pipeline;
    private readonly RepositoryResolverService _resolver;

    public PipelineController(ISolutionAnalysisPipeline pipeline, RepositoryResolverService resolver)
    {
        _pipeline = pipeline;
        _resolver = resolver;
    }

    [HttpPost("full-analysis")]
    [ProducesResponseType(typeof(FullAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunFullAnalysis(
        [FromBody] PipelineRequestDto request, CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _pipeline.RunFullAnalysisAsync(
                resolved.LocalPath!,
                request.Options,
                cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    // BUG-02 FIX: PipelineRequestDto-klassen som tidigare låg inline här
    // är borttagen. Den finns nu kanoniskt i Synthtax.Core/DTOs/AdditionalDtos.cs.
}
