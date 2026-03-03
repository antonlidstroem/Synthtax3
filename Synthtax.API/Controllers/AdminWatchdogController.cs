using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Filters;
using Synthtax.API.SuperAdmin.DTOs;
using Synthtax.Application.Watchdog;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;
using Synthtax.Infrastructure.Services;

namespace Synthtax.API.Controllers;

/// <summary>
/// API för watchdog-status och manuella triggers.
///
/// <para><b>Routing:</b> <c>api/v1/admin/watchdog</c></para>
/// </summary>
[Authorize]
[RequireSystemAdmin]
[ApiController]
[Route("api/v1/admin/watchdog")]
[Produces("application/json")]
public sealed class AdminWatchdogController : ControllerBase
{
    private readonly IEnumerable<IWatchdogSourceChecker> _checkers;
    private readonly IAlertService                       _alerts;
    private readonly IWatchdogRunRepository              _runs;

    public AdminWatchdogController(
        IEnumerable<IWatchdogSourceChecker> checkers,
        IAlertService alerts,
        IWatchdogRunRepository runs)
    {
        _checkers = checkers;
        _alerts   = alerts;
        _runs     = runs;
    }

    // ── GET /api/v1/admin/watchdog/status ─────────────────────────────────
    [HttpGet("status")]
    [ProducesResponseType(typeof(WatchdogStatusResponse), 200)]
    public async Task<IActionResult> GetStatus(CancellationToken ct = default)
    {
        var sources = new List<WatchdogStatusDto>();

        foreach (var checker in _checkers)
        {
            var lastRun   = await _runs.GetLastRunAsync(checker.Source, ct);
            var newAlerts = await _alerts.CountNewAlertsAsync(checker.Source, hours: 24, ct);

            sources.Add(new WatchdogStatusDto
            {
                Source           = checker.Source.ToString(),
                IsEnabled        = true,
                LastRunAt        = lastRun?.RanAt,
                LastRunOk        = lastRun?.Success,
                LastError        = lastRun?.ErrorMessage,
                NewAlertsLast24h = newAlerts,
                NextScheduledRun = lastRun?.RanAt.Add(checker.CheckInterval)
            });
        }

        var totalNew      = sources.Sum(s => s.NewAlertsLast24h);
        var totalCritical = await _alerts.CountCriticalNewAsync(ct);

        return Ok(new WatchdogStatusResponse
        {
            Sources        = sources,
            TotalNewAlerts = totalNew,
            TotalCritical  = totalCritical
        });
    }

    // ── POST /api/v1/admin/watchdog/{source}/trigger ─────────────────────
    [HttpPost("{source}/trigger")]
    [ProducesResponseType(typeof(ManualTriggerResponse), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> TriggerManualCheck(
        string source,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<WatchdogSource>(source, ignoreCase: true, out var sourceEnum))
            return BadRequest(new ProblemDetails
            {
                Title  = "Unknown source",
                Detail = $"'{source}' is not a recognized watchdog source.",
                Status = 400
            });

        var checker = _checkers.FirstOrDefault(c => c.Source == sourceEnum);
        if (checker is null) return NotFound();

        var sw       = System.Diagnostics.Stopwatch.StartNew();
        var findings = await checker.CheckAsync(ct);
        sw.Stop();

        int newAlerts = 0;
        foreach (var finding in findings)
        {
            var created = await _alerts.CreateFromFindingAsync(finding, ct);
            if (created) newAlerts++;
        }

        return Ok(new ManualTriggerResponse
        {
            Source        = source,
            FindingsCount = findings.Count,
            NewAlerts     = newAlerts,
            DurationMs    = (int)sw.ElapsedMilliseconds,
            RanAt         = DateTime.UtcNow
        });
    }

    // ── GET /api/v1/admin/watchdog/runs ──────────────────────────────────
    [HttpGet("runs")]
    [ProducesResponseType(typeof(IReadOnlyList<WatchdogRunDto>), 200)]
    public async Task<IActionResult> GetRuns(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var runs = await _runs.GetRecentRunsAsync(Math.Min(limit, 200), ct);
        var dtos = runs.Select(r => new WatchdogRunDto
        {
            Source       = r.Source.ToString(),
            Success      = r.Success,
            ErrorMessage = r.ErrorMessage,
            NewAlerts    = r.NewAlerts,
            DurationMs   = r.DurationMs,
            RanAt        = r.RanAt
        }).ToList();

        return Ok(dtos);
    }
}

// ── Response-typer ─────────────────────────────────────────────────────────

public sealed record ManualTriggerResponse
{
    public required string   Source        { get; init; }
    public required int      FindingsCount { get; init; }
    public required int      NewAlerts     { get; init; }
    public required int      DurationMs    { get; init; }
    public required DateTime RanAt         { get; init; }
}

public sealed record WatchdogRunDto
{
    public required string   Source       { get; init; }
    public required bool     Success      { get; init; }
    public          string?  ErrorMessage { get; init; }
    public required int      NewAlerts    { get; init; }
    public required int      DurationMs   { get; init; }
    public required DateTime RanAt        { get; init; }
}
