using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
using Synthtax.API.Services.Analysis;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [Produces("application/json")]
    public class CouplingController : ControllerBase
    {
        private readonly ICouplingAnalysisService _couplingService;
        private readonly RepositoryResolverService _resolver;

        public CouplingController(ICouplingAnalysisService couplingService, RepositoryResolverService resolver)
        {
            _couplingService = couplingService;
            _resolver = resolver;
        }

        [HttpPost("analyze")]
        [ProducesResponseType(typeof(CouplingAnalysisResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Analyze(
            [FromBody] AnalysisRequestDto request, CancellationToken cancellationToken)
        {
            var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
            if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

            try
            {
                var result = await _couplingService.AnalyzeSolutionAsync(resolved.LocalPath!, cancellationToken);
                return Ok(result);
            }
            finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
        }
    }
}
