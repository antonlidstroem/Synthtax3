using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Synthtax.API.Filters;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Services;

namespace Synthtax.API.Controllers;

/// <summary>
/// API för hantering av watchdog-larm i super-admin-panelen.
///
/// <para><b>Routing:</b> <c>api/v1/admin/alerts</c></para>
///
/// <para><b>Push-notiser:</b>
/// När en alert bekräftas eller löses skickas ett SignalR-event
/// (<c>WatchdogAlertUpdated</c>) till alla inloggade super-admins.</para>
/// </summary>
[Authorize]
[RequireSystemAdmin]
[ApiController]
[Route("api/v1/admin/alerts")]
[Produces("application/json")]
public sealed class AdminAlertController : ControllerBase
{
    private readonly IAlertService                  _alerts;
    private readonly IHubContext<AdminHub>           _hub;

    public AdminAlertController(
        IAlertService         alerts,
        IHubContext<AdminHub> hub)
    {
        _alerts = alerts;
        _hub    = hub;
    }

    // ── GET /api/v1/admin/alerts ───────────────────────────────────────────
    /// <summary>
    /// Hämtar alla larm, sorterade efter skapelsedatum (nyaste först).
    /// Filtrerbar på status och severity.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AlertListResponse), 200)]
    public async Task<IActionResult> ListAlerts(
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 25,
        [FromQuery] string? status     = null,
        [FromQuery] string? severity   = null,
        [FromQuery] string? source     = null,
        CancellationToken ct = default)
    {
        var result = await _alerts.ListAlertsAsync(
            page, pageSize, status, severity, source, ct);
        return Ok(result);
    }

    // ── GET /api/v1/admin/alerts/summary ──────────────────────────────────
    /// <summary>
    /// Snabbsammanfattning: antal nya/kritiska larm per källa.
    /// Används i dashboardens header-badges.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AlertSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct = default)
    {
        var summary = await _alerts.GetSummaryAsync(ct);
        return Ok(summary);
    }

    // ── GET /api/v1/admin/alerts/{id} ─────────────────────────────────────
    /// <summary>Hämtar ett enskilt larm med fullständig payload.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AlertDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetAlert(Guid id, CancellationToken ct = default)
    {
        var alert = await _alerts.GetByIdAsync(id, ct);
        return alert is null ? NotFound() : Ok(alert);
    }

    // ── PATCH /api/v1/admin/alerts/{id}/status ───────────────────────────
    /// <summary>
    /// Uppdaterar status för ett larm: Acknowledged, Resolved eller Dismissed.
    /// Skickar SignalR <c>WatchdogAlertUpdated</c> till super-admin-panelen.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(AlertDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> UpdateAlertStatus(
        Guid id,
        [FromBody] UpdateAlertStatusRequest request,
        CancellationToken ct = default)
    {
        var validStatuses = new[] { "Acknowledged", "Resolved", "Dismissed" };
        if (!validStatuses.Contains(request.NewStatus, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails
            {
                Title  = "Invalid status",
                Detail = $"Status must be one of: {string.Join(", ", validStatuses)}",
                Status = 400
            });

        try
        {
            var updatedBy = User.Identity?.Name ?? "system";
            var updated   = await _alerts.UpdateStatusAsync(
                id, request.NewStatus, request.Comment, updatedBy, ct);

            if (updated is null) return NotFound();

            // Push till alla inloggade super-admins
            await _hub.Clients.Group(AdminAlertHubPublisher.AdminGroupName)
                .SendAsync(AdminAlertHubPublisher.AlertUpdatedMethod, new
                {
                    id        = updated.Id,
                    newStatus = updated.Status.ToString(),
                    updatedBy
                }, ct);

            return Ok(MapToDto(updated));
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── POST /api/v1/admin/alerts/dismiss-all-info ────────────────────────
    /// <summary>
    /// Bulk-dismiss av alla Info-larm som är äldre än 30 dagar.
    /// Håller listan ren utan manuellt arbete.
    /// </summary>
    [HttpPost("dismiss-all-info")]
    [ProducesResponseType(typeof(BulkDismissResponse), 200)]
    public async Task<IActionResult> DismissOldInfoAlerts(CancellationToken ct = default)
    {
        var count = await _alerts.BulkDismissInfoAlertsAsync(
            olderThanDays: 30,
            by: User.Identity?.Name ?? "system",
            ct);
        return Ok(new BulkDismissResponse { DismissedCount = count });
    }

    // ── Mappning ──────────────────────────────────────────────────────────

    private static AlertDto MapToDto(WatchdogAlert a) => new()
    {
        Id                  = a.Id,
        Source              = a.Source.ToString(),
        Severity            = a.Severity.ToString(),
        Status              = a.Status.ToString(),
        Title               = a.Title,
        Description         = a.Description,
        ReleaseNotesUrl     = a.ReleaseNotesUrl,
        ActionRequired      = a.ActionRequired,
        ExternalVersionKey  = a.ExternalVersionKey,
        ExternalPublishedAt = a.ExternalPublishedAt,
        CreatedAt           = a.CreatedAt,
        AcknowledgedBy      = a.AcknowledgedBy,
        AcknowledgedAt      = a.AcknowledgedAt
    };
}

public sealed record BulkDismissResponse { public int DismissedCount { get; init; } }

/// <summary>Fullständig alert-vy med råpayload.</summary>
public sealed record AlertDetailDto : AlertDto
{
    public string? RawPayloadJson { get; init; }
    public string? ResolvedBy     { get; init; }
    public DateTime? ResolvedAt   { get; init; }
}

/// <summary>Badge-sammanfattning för dashboard-header.</summary>
public sealed record AlertSummaryDto
{
    public int NewCount      { get; init; }
    public int CriticalCount { get; init; }
    public int WarningCount  { get; init; }
    public int TotalOpen     { get; init; }
}
