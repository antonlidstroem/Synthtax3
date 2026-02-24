using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class GitController : ControllerBase
{
    private readonly IGitAnalysisService _gitService;

    public GitController(IGitAnalysisService gitService)
    {
        _gitService = gitService;
    }

    /// <summary>Kör full Git-analys: commits, brancher, churn, bus factor.</summary>
    [HttpGet("analyze")]
    [ProducesResponseType(typeof(GitAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze(
        [FromQuery] string repositoryPath,
        [FromQuery] int maxCommits = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath is required." });

        if (!_gitService.IsValidRepository(repositoryPath))
            return BadRequest(new { Message = $"'{repositoryPath}' is not a valid Git repository." });

        var result = await _gitService.AnalyzeRepositoryAsync(repositoryPath, maxCommits, cancellationToken);
        return Ok(result);
    }

    /// <summary>Hämtar commits.</summary>
    [HttpGet("commits")]
    [ProducesResponseType(typeof(List<GitCommitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommits(
        [FromQuery] string repositoryPath,
        [FromQuery] int maxCommits = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath is required." });

        var result = await _gitService.GetCommitsAsync(repositoryPath, maxCommits, cancellationToken);
        return Ok(result);
    }

    /// <summary>Hämtar alla brancher.</summary>
    [HttpGet("branches")]
    [ProducesResponseType(typeof(List<GitBranchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBranches(
        [FromQuery] string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath is required." });

        var result = await _gitService.GetBranchesAsync(repositoryPath, cancellationToken);
        return Ok(result);
    }

    /// <summary>Beräknar fil-churn.</summary>
    [HttpGet("churn")]
    [ProducesResponseType(typeof(List<GitChurnDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChurn(
        [FromQuery] string repositoryPath,
        [FromQuery] int maxCommits = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath is required." });

        var result = await _gitService.GetFileChurnAsync(repositoryPath, maxCommits, cancellationToken);
        return Ok(result);
    }

    /// <summary>Beräknar bus factor.</summary>
    [HttpGet("bus-factor")]
    [ProducesResponseType(typeof(List<BusFactorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBusFactor(
        [FromQuery] string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath is required." });

        var result = await _gitService.GetBusFactorAsync(repositoryPath, cancellationToken);
        return Ok(result);
    }
}
