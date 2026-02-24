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
public class MethodExplorerController : ControllerBase
{
    private readonly IMethodExplorerService _methodExplorerService;
    private readonly RepositoryResolverService _resolver;

    public MethodExplorerController(
        IMethodExplorerService methodExplorerService,
        RepositoryResolverService resolver)
    {
        _methodExplorerService = methodExplorerService;
        _resolver = resolver;
    }

    /// <summary>
    /// Listar alla metoder i en solution.
    /// Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL.
    /// </summary>
    [HttpGet("methods")]
    [ProducesResponseType(typeof(MethodExplorerResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllMethods(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _methodExplorerService.GetAllMethodsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>Söker efter metoder som matchar ett mönster.</summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(List<MethodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchMethods(
        [FromQuery] string solutionPath,
        [FromQuery] string pattern,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionPath) || string.IsNullOrWhiteSpace(pattern))
            return BadRequest(new { Message = "solutionPath and pattern are required." });

        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _methodExplorerService.SearchMethodsAsync(
                resolved.LocalPath!, pattern, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>Hämtar metoder för en specifik klass.</summary>
    [HttpGet("class/{className}")]
    [ProducesResponseType(typeof(List<MethodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMethodsForClass(
        string className,
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _methodExplorerService.GetMethodsForClassAsync(
                resolved.LocalPath!, className, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
