using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;

namespace Synthtax.API.Controllers;

/// <summary>
/// Pull Request-integration (stub).
/// Returnerar demo-data eller vidarebefordrar till konfigurerad Git-forge (GitHub/GitLab/Azure DevOps).
/// Implementera IForgeClient och registrera i DI för riktig integration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PullRequestsController : ControllerBase
{
    private readonly ILogger<PullRequestsController> _logger;

    public PullRequestsController(ILogger<PullRequestsController> logger)
    {
        _logger = logger;
    }

    /// <summary>Hämtar pull requests för ett repository. Returnerar tom lista om ingen integration är konfigurerad.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<PullRequestDto>), StatusCodes.Status200OK)]
    public IActionResult GetPullRequests(
        [FromQuery] string? repositoryUrl,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        _logger.LogInformation("PR request for repository: {Url}", repositoryUrl);

        // No forge integration configured → return empty list;
        // WPF client will use built-in demo data when empty.
        return Ok(new List<PullRequestDto>());
    }

    /// <summary>Hämtar en specifik pull request.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(PullRequestDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetPullRequest(int id)
    {
        return NotFound(new { Message = "Forge-integration ej konfigurerad." });
    }
}
