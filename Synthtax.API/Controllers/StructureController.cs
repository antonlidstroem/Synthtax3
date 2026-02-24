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
public class StructureController : ControllerBase
{
    private readonly IStructureAnalysisService _structureService;
    private readonly RepositoryResolverService _resolver;

    public StructureController(
        IStructureAnalysisService structureService,
        RepositoryResolverService resolver)
    {
        _structureService = structureService;
        _resolver = resolver;
    }

    /// <summary>
    /// Trädvy av solution-strukturen. Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(StructureAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStructure(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _structureService.AnalyzeSolutionStructureAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
