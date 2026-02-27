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
public class GitController : ControllerBase
{
    private readonly IGitAnalysisService _gitService;
    private readonly RepositoryResolverService _resolver;

    public GitController(
        IGitAnalysisService gitService,
        RepositoryResolverService resolver)
    {
        _gitService = gitService;
        _resolver = resolver;
    }

    /// <summary>
    /// Fullständig Git-analys (commits, branches, churn, bus-factor).
    /// Accepterar lokal repo-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("analyze")]
    [ProducesResponseType(typeof(GitAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Analyze(
        [FromQuery] string repositoryPath,
        [FromQuery] int maxCommits = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath saknas." });

        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        if (!_gitService.IsValidRepository(resolved.LocalPath!))
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
            return BadRequest(new { Message = $"'{resolved.LocalPath}' är inte ett giltigt Git-repo." });
        }

        try
        {
            var result = await _gitService.AnalyzeRepositoryAsync(
                resolved.LocalPath!, maxCommits, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hämtar commits. Accepterar lokal repo-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("commits")]
    [ProducesResponseType(typeof(List<GitCommitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommits(
        [FromQuery] string repositoryPath,
        [FromQuery] int maxCommits = 100,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _gitService.GetCommitsAsync(
                resolved.LocalPath!, maxCommits, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hämtar branches. Accepterar lokal repo-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("branches")]
    [ProducesResponseType(typeof(List<GitBranchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBranches(
        [FromQuery] string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _gitService.GetBranchesAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Hämtar file churn. Accepterar lokal repo-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("churn")]
    [ProducesResponseType(typeof(List<GitChurnDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChurn(
        [FromQuery] string repositoryPath,
        [FromQuery] int maxCommits = 200,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _gitService.GetFileChurnAsync(
                resolved.LocalPath!, maxCommits, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    /// <summary>
    /// Beräknar bus-factor. Accepterar lokal repo-sökväg eller GitHub-URL.
    /// </summary>
    [HttpGet("bus-factor")]
    [ProducesResponseType(typeof(List<BusFactorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBusFactor(
        [FromQuery] string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _gitService.GetBusFactorAsync(
                resolved.LocalPath!, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
