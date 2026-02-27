using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;


// ─────────────────────────────────────────────────────────────────────────────
// PipelineController — single endpoint that loads solution once and returns
// all analysis results in one call (replaces calling 5 endpoints separately)
// ─────────────────────────────────────────────────────────────────────────────
namespace Synthtax.API.Controllers
{
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

        /// <summary>
        /// Runs ALL configured analyses in parallel with a single solution load.
        /// More efficient than calling individual analysis endpoints for the same solution.
        /// </summary>
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
    }

    public class PipelineRequestDto
    {
        public string? SolutionPath { get; set; }
        public FullAnalysisOptions? Options { get; set; }
    }
}
