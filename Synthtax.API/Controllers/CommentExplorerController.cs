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
    private readonly ICommentExplorerService _commentExplorerService;
    private readonly RepositoryResolverService _resolver;

    public CommentExplorerController(
        ICommentExplorerService commentExplorerService,
        RepositoryResolverService resolver)
    {
        _commentExplorerService = commentExplorerService;
        _resolver = resolver;
    }

    /// <summary>
    /// Hämtar alla kommentarer i solutionen.
    /// Accepterar lokal .sln-sökväg, lokal mapp eller GitHub-URL.
    /// </summary>
    [HttpGet("comments")]
    [ProducesResponseType(typeof(CommentExplorerResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllComments(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _commentExplorerService.GetAllCommentsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hämtar enbart TODO/FIXME/HACK-kommentarer.
    /// Accepterar lokal .sln-sökväg, lokal mapp eller GitHub-URL.
    /// </summary>
    [HttpGet("todos")]
    [ProducesResponseType(typeof(List<CommentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTodos(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _commentExplorerService.GetTodoCommentsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hämtar alla #region-block.
    /// Accepterar lokal .sln-sökväg, lokal mapp eller GitHub-URL.
    /// </summary>
    [HttpGet("regions")]
    [ProducesResponseType(typeof(List<RegionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegions(
        [FromQuery] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveAsync(solutionPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _commentExplorerService.GetRegionsAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
