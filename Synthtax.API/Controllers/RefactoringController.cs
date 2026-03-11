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
    public class RefactoringController : ControllerBase
    {
        private readonly IRefactoringService _refactoringService;
        private readonly RepositoryResolverService _resolver;

        public RefactoringController(IRefactoringService refactoringService, RepositoryResolverService resolver)
        {
            _refactoringService = refactoringService;
            _resolver = resolver;
        }

        [HttpPost("solution")]
        [ProducesResponseType(typeof(RefactoringResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SuggestForSolution(
            [FromBody] AnalysisRequestDto request, CancellationToken cancellationToken)
        {
            var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
            if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

            try
            {
                var result = await _refactoringService.SuggestRefactoringsAsync(
                    resolved.LocalPath!, cancellationToken);
                return Ok(result);
            }
            finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
        }

        [HttpPost("code")]
        [ProducesResponseType(typeof(RefactoringResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> SuggestForCode(
            [FromBody] AnalyzeCodeRequestDto request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
                return BadRequest(new { Message = "Code is required." });

            var result = await _refactoringService.SuggestRefactoringsForCodeAsync(
                request.Code, request.FileName ?? "input.cs", cancellationToken);
            return Ok(result);
        }
    }

}
