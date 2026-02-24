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
public class CommentExplorerController : ControllerBase
{
    private readonly ICommentExplorerService _commentService;
    private readonly RepositoryResolverService _resolver;

    public CommentExplorerController(
        ICommentExplorerService commentService,
        RepositoryResolverService resolver)
    {
        _commentService = commentService;
        _resolver = resolver;
    }

    /// <summary>
    /// Hämtar alla kommentarer och regioner.
    /// Accepterar lokal .sln-sökväg ELLER GitHub/GitLab-URL.
    /// </summary>
    [HttpGet("all")]
    [ProducesResponseType(typeof(CommentExplorerResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _commentService.GetAllCommentsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Hämtar TODO/FIXME/HACK-kommentarer.</summary>
    [HttpGet("todos")]
    [ProducesResponseType(typeof(List<CommentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTodos(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _commentService.GetTodoCommentsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    /// <summary>Hämtar alla #region-definitioner.</summary>
    [HttpGet("regions")]
    [ProducesResponseType(typeof(List<RegionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegions(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _commentService.GetRegionsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }
}
