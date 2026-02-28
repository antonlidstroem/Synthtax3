using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

/// <summary>
/// Web language analysis: CSS, JavaScript and HTML.
/// New languages are added by registering an ILanguagePlugin – no changes here.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class WebAnalysisController : SynthtaxControllerBase
{
    private readonly IWebLanguageAnalysisService _service;
    private readonly RepositoryResolverService   _resolver;

    public WebAnalysisController(
        IWebLanguageAnalysisService service,
        RepositoryResolverService resolver)
    {
        _service  = service;
        _resolver = resolver;
    }

    // ── GET api/web-analysis/plugins ─────────────────────────────────────────

    /// <summary>Lists all registered language plugins and their rules.</summary>
    [HttpGet("plugins")]
    [ProducesResponseType(typeof(List<LanguagePluginInfoDto>), StatusCodes.Status200OK)]
    public IActionResult GetPlugins()
        => Ok(_service.GetRegisteredPlugins());

    // ── GET api/web-analysis/analyze ─────────────────────────────────────────

    /// <summary>
    /// Analyses all CSS, JavaScript and HTML files under a directory.
    /// Pass the root of your frontend project (e.g. ClientApp/ or wwwroot/).
    /// </summary>
    [HttpGet("analyze")]
    [ProducesResponseType(typeof(WebAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeDirectory(
        [FromQuery] string projectPath,
        [FromQuery] bool   recursive = true,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return BadRequest(new { Message = "projectPath is required." });

        var resolved = await _resolver.ResolveDirectoryAsync(projectPath, cancellationToken);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            var result = await _service.AnalyzeDirectoryAsync(
                resolved.LocalPath!, recursive, cancellationToken);
            return Ok(result);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }

    // ── GET api/web-analysis/analyze-file ────────────────────────────────────

    /// <summary>Analyses a single CSS, JavaScript or HTML file.</summary>
    [HttpGet("analyze-file")]
    [ProducesResponseType(typeof(WebFileResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeFile(
        [FromQuery] string filePath,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return BadRequest(new { Message = "filePath is required." });

        if (!System.IO.File.Exists(filePath))
            return BadRequest(new { Message = $"File not found: {filePath}" });

        var result = await _service.AnalyzeFileAsync(filePath, cancellationToken);
        return Ok(result);
    }

    // ── GET api/web-analysis/css ──────────────────────────────────────────────

    /// <summary>
    /// CSS-only analysis. Includes cross-file unused-selector detection
    /// (compares CSS selectors against class/id usage in HTML and JS files).
    /// </summary>
    [HttpGet("css")]
    [ProducesResponseType(typeof(WebAnalysisResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AnalyzeCss(
        [FromQuery] string projectPath,
        [FromQuery] bool   recursive = true,
        CancellationToken  cancellationToken = default)
        => await AnalyzeByLanguage(projectPath, "CSS", recursive, cancellationToken);

    // ── GET api/web-analysis/javascript ──────────────────────────────────────

    /// <summary>JavaScript and TypeScript analysis.</summary>
    [HttpGet("javascript")]
    [ProducesResponseType(typeof(WebAnalysisResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeJavaScript(
        [FromQuery] string projectPath,
        [FromQuery] bool   recursive = true,
        CancellationToken  cancellationToken = default)
        => await AnalyzeByLanguage(projectPath, "JavaScript", recursive, cancellationToken);

    // ── GET api/web-analysis/html ─────────────────────────────────────────────

    /// <summary>HTML analysis (accessibility, deprecated tags, meta tags, etc.).</summary>
    [HttpGet("html")]
    [ProducesResponseType(typeof(WebAnalysisResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeHtml(
        [FromQuery] string projectPath,
        [FromQuery] bool   recursive = true,
        CancellationToken  cancellationToken = default)
        => await AnalyzeByLanguage(projectPath, "HTML", recursive, cancellationToken);

    // ── Shared helper ─────────────────────────────────────────────────────────

    private async Task<IActionResult> AnalyzeByLanguage(
        string projectPath, string language, bool recursive, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return BadRequest(new { Message = "projectPath is required." });

        var resolved = await _resolver.ResolveDirectoryAsync(projectPath, ct);
        if (!resolved.Success)
            return BadRequest(new { Message = resolved.ErrorMessage });

        try
        {
            // Run full analysis and return only the requested language slice
            var full = await _service.AnalyzeDirectoryAsync(resolved.LocalPath!, recursive, ct);
            var filtered = new WebAnalysisResultDto
            {
                ProjectPath   = full.ProjectPath,
                AnalyzedAt    = full.AnalyzedAt,
                FileResults   = full.FileResults.Where(r => r.Language == language).ToList(),
                Errors        = full.Errors
            };
            filtered.FilesAnalyzed  = filtered.FileResults.Count;
            filtered.TotalIssues    = filtered.FileResults.Sum(r => r.IssueCount);
            var all = filtered.FileResults.SelectMany(r => r.Issues).ToList();
            filtered.CriticalCount  = all.Count(i => i.Severity == Core.Enums.Severity.Critical);
            filtered.HighCount      = all.Count(i => i.Severity == Core.Enums.Severity.High);
            filtered.MediumCount    = all.Count(i => i.Severity == Core.Enums.Severity.Medium);
            filtered.LowCount       = all.Count(i => i.Severity == Core.Enums.Severity.Low);
            if (all.Any()) filtered.ByLanguage[language] = all;
            return Ok(filtered);
        }
        finally
        {
            if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
        }
    }
}
