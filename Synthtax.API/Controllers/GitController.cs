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
    private readonly IGitAnalysisService     _gitService;
    private readonly ICommitMessageService   _commitService;
    private readonly RepositoryResolverService _resolver;

    public GitController(
        IGitAnalysisService gitService,
        ICommitMessageService commitService,
        RepositoryResolverService resolver)
    {
        _gitService    = gitService;
        _commitService = commitService;
        _resolver      = resolver;
    }

    // ── Existing endpoints (unchanged) ────────────────────────────────────────

    [HttpGet("analyze")]
    [ProducesResponseType(typeof(GitAnalysisResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Analyze(
        [FromQuery] string repositoryPath,
        [FromQuery] int    maxCommits = 100,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath saknas." });

        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        if (!_gitService.IsValidRepository(resolved.LocalPath!))
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
            return BadRequest(new { Message = $"'{resolved.LocalPath}' är inte ett giltigt Git-repo." });
        }
        try
        {
            return Ok(await _gitService.AnalyzeRepositoryAsync(resolved.LocalPath!, maxCommits, cancellationToken));
        }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("commits")]
    [ProducesResponseType(typeof(List<GitCommitDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCommits(
        [FromQuery] string repositoryPath,
        [FromQuery] int    maxCommits = 100,
        CancellationToken  cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try { return Ok(await _gitService.GetCommitsAsync(resolved.LocalPath!, maxCommits, cancellationToken)); }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("branches")]
    [ProducesResponseType(typeof(List<GitBranchDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBranches(
        [FromQuery] string repositoryPath,
        CancellationToken  cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try { return Ok(await _gitService.GetBranchesAsync(resolved.LocalPath!, cancellationToken)); }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("churn")]
    [ProducesResponseType(typeof(List<GitChurnDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChurn(
        [FromQuery] string repositoryPath,
        [FromQuery] int    maxCommits = 200,
        CancellationToken  cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try { return Ok(await _gitService.GetFileChurnAsync(resolved.LocalPath!, maxCommits, cancellationToken)); }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    [HttpGet("bus-factor")]
    [ProducesResponseType(typeof(List<BusFactorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBusFactor(
        [FromQuery] string repositoryPath,
        CancellationToken  cancellationToken = default)
    {
        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });
        try { return Ok(await _gitService.GetBusFactorAsync(resolved.LocalPath!, cancellationToken)); }
        finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
    }

    // ── NEW: commit message suggestion ────────────────────────────────────────

    /// <summary>
    /// Compares uncommitted changes against HEAD and returns a rule-based
    /// Conventional Commits message suggestion. No AI involved.
    /// </summary>
    /// <param name="repositoryPath">Path to the local Git repository.</param>
    /// <param name="stagedOnly">
    /// When true, only staged changes (index vs HEAD) are analysed.
    /// When false (default), all uncommitted changes are analysed.
    /// </param>
    [HttpGet("commit-suggestion")]
    [ProducesResponseType(typeof(CommitSuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCommitSuggestion(
        [FromQuery] string repositoryPath,
        [FromQuery] bool   stagedOnly = false,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return BadRequest(new { Message = "repositoryPath is required." });

        var resolved = await _resolver.ResolveDirectoryAsync(repositoryPath, cancellationToken);
        if (!resolved.Success) return BadRequest(new { Message = resolved.ErrorMessage });

        if (!_gitService.IsValidRepository(resolved.LocalPath!))
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
            return BadRequest(new { Message = $"'{resolved.LocalPath}' is not a valid Git repository." });
        }

        try
        {
            var suggestion = await _commitService.SuggestAsync(
                resolved.LocalPath!, stagedOnly, cancellationToken);

            if (suggestion.Errors.Count > 0 && string.IsNullOrEmpty(suggestion.Subject))
                return BadRequest(new { suggestion.Errors });

            return Ok(suggestion);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
