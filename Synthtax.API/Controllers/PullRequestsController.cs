using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;

namespace Synthtax.API.Controllers;

/// <summary>
/// Placeholder för forge-integration (GitHub, GitLab, Azure DevOps).
/// Returnerar 501 Not Implemented tills integrationen är implementerad.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class PullRequestsController : ControllerBase
{
    private readonly ILogger<PullRequestsController> _logger;

    public PullRequestsController(ILogger<PullRequestsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lista pull requests för ett repository.
    /// OBS: Forge-integration är inte implementerad ännu.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<PullRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult GetPullRequests(
        [FromQuery] string? repositoryUrl,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        _logger.LogInformation("PR request for repository: {Url} – not yet implemented", repositoryUrl);

        return StatusCode(StatusCodes.Status501NotImplemented, new
        {
            Message       = "Pull request integration is not yet configured.",
            SupportedForges = new[] { "GitHub", "GitLab", "Azure DevOps" },
            Documentation = "Configure forge integration in appsettings.json under 'ForgeIntegration'.",
            // Returneras så klienter kan skilja på 'feature unavailable' vs 'no PRs found'
            IsPlaceholder = true
        });
    }

    /// <summary>
    /// Hämta en specifik pull request.
    /// OBS: Forge-integration är inte implementerad ännu.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PullRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public IActionResult GetPullRequest(int id)
    {
        return StatusCode(StatusCodes.Status501NotImplemented, new
        {
            Message       = "Pull request integration is not yet configured.",
            SupportedForges = new[] { "GitHub", "GitLab", "Azure DevOps" },
            IsPlaceholder = true
        });
    }
}
