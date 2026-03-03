using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Application.Watchdog;
using Synthtax.Core.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

public interface IAlertService
{
    Task<AlertListResponse> ListAlertsAsync(
        int page, int pageSize,
        string? statusFilter, string? severityFilter, string? sourceFilter,
        CancellationToken ct = default);

    Task<AlertSummaryDto>  GetSummaryAsync(CancellationToken ct = default);
    Task<WatchdogAlert?>   GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<WatchdogAlert?> UpdateStatusAsync(
        Guid id, string newStatus, string? comment,
        string updatedBy, CancellationToken ct = default);

    Task<bool> CreateFromFindingAsync(WatchdogFinding finding, CancellationToken ct = default);

    Task<int> BulkDismissInfoAlertsAsync(int olderThanDays, string by, CancellationToken ct = default);
    Task<int> CountNewAlertsAsync(WatchdogSource source, int hours, CancellationToken ct = default);
    Task<int> CountCriticalNewAsync(CancellationToken ct = default);
}

public sealed class AlertServiceImpl : IAlertService
{
    private readonly SynthtaxDbContext         _db;
    private readonly IAdminAlertPublisher      _publisher;
    private readonly ILogger<AlertServiceImpl> _logger;

    public AlertServiceImpl(
        SynthtaxDbContext db,
        IAdminAlertPublisher publisher,
        ILogger<AlertServiceImpl> logger)
    {
        _db        = db;
        _publisher = publisher;
        _logger    = logger;
    }

    public async Task<AlertListResponse> ListAlertsAsync(
        int page, int pageSize,
        string? statusFilter, string? severityFilter, string? sourceFilter,
        CancellationToken ct = default)
    {
        var query = _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(statusFilter) &&
            Enum.TryParse<AlertStatus>(statusFilter, ignoreCase: true, out var status))
            query = query.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(severityFilter) &&
            Enum.TryParse<AlertSeverity>(severityFilter, ignoreCase: true, out var sev))
            query = query.Where(a => a.Severity == sev);

        if (!string.IsNullOrWhiteSpace(sourceFilter) &&
            Enum.TryParse<WatchdogSource>(sourceFilter, ignoreCase: true, out var src))
            query = query.Where(a => a.Source == src);

        var total    = await query.CountAsync(ct);
        var newCount = await query.CountAsync(a => a.Status == AlertStatus.New, ct);
        var critCount = await query.CountAsync(
            a => a.Severity == AlertSeverity.Critical && a.Status == AlertStatus.New, ct);

        var alerts = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new AlertListResponse
        {
            Items         = alerts.Select(MapToDto).ToList(),
            TotalCount    = total,
            NewCount      = newCount,
            CriticalCount = critCount
        };
    }

    public async Task<AlertSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var query = _db.WatchdogAlerts.IgnoreQueryFilters().AsNoTracking();
        return new AlertSummaryDto
        {
            NewCount      = await query.CountAsync(a => a.Status == AlertStatus.New, ct),
            CriticalCount = await query.CountAsync(
                a => a.Severity == AlertSeverity.Critical && a.Status == AlertStatus.New, ct),
            WarningCount  = await query.CountAsync(
                a => a.Severity == AlertSeverity.Warning  && a.Status == AlertStatus.New, ct),
            TotalOpen     = await query.CountAsync(
                a => a.Status == AlertStatus.New || a.Status == AlertStatus.Acknowledged, ct)
        };
    }

    public async Task<WatchdogAlert?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<bool> CreateFromFindingAsync(
        WatchdogFinding finding, CancellationToken ct = default)
    {
        var exists = await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Source              == finding.Source
                        && a.ExternalVersionKey  == finding.ExternalVersionKey, ct);

        if (exists)
        {
            _logger.LogDebug(
                "Alert already exists for {Source}/{Key} — skipping.",
                finding.Source, finding.ExternalVersionKey);
            return false;
        }

        var alert = new WatchdogAlert
        {
            Id                  = Guid.NewGuid(),
            Source              = finding.Source,
            Severity            = finding.Severity,
            Status              = AlertStatus.New,
            ExternalVersionKey  = finding.ExternalVersionKey,
            Title               = finding.Title,
            Description         = finding.Description,
            ReleaseNotesUrl     = finding.ReleaseNotesUrl,
            ActionRequired      = finding.ActionRequired,
            ExternalPublishedAt = finding.ExternalPublishedAt,
            RawPayloadJson      = finding.RawPayloadJson
        };

        _db.WatchdogAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "WatchdogAlert created: [{Sev}] {Title} ({Key})",
            finding.Severity, finding.Title, finding.ExternalVersionKey);

        await _publisher.PublishNewAlertAsync(alert, ct);
        return true;
    }

    public async Task<WatchdogAlert?> UpdateStatusAsync(
        Guid id, string newStatusStr, string? comment,
        string updatedBy, CancellationToken ct = default)
    {
        if (!Enum.TryParse<AlertStatus>(newStatusStr, ignoreCase: true, out var newStatus))
            throw new ArgumentException($"Unknown status '{newStatusStr}'.");

        var alert = await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (alert is null) return null;

        alert.Status = newStatus;
        switch (newStatus)
        {
            case AlertStatus.Acknowledged:
                alert.AcknowledgedBy = updatedBy;
                alert.AcknowledgedAt = DateTime.UtcNow;
                break;
            case AlertStatus.Resolved:
                alert.ResolvedBy = updatedBy;
                alert.ResolvedAt = DateTime.UtcNow;
                break;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Alert {Id} status → {Status} by {By}", id, newStatus, updatedBy);
        return alert;
    }

    public async Task<int> BulkDismissInfoAlertsAsync(
        int olderThanDays, string by, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var toUpdate = await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .Where(a => a.Severity == AlertSeverity.Info
                     && a.Status   == AlertStatus.New
                     && a.CreatedAt < cutoff)
            .ToListAsync(ct);

        foreach (var a in toUpdate)
        {
            a.Status     = AlertStatus.Dismissed;
            a.ResolvedBy = by;
            a.ResolvedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return toUpdate.Count;
    }

    public async Task<int> CountNewAlertsAsync(
        WatchdogSource source, int hours, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-hours);
        return await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .CountAsync(a => a.Source == source
                          && a.Status == AlertStatus.New
                          && a.CreatedAt >= cutoff, ct);
    }

    public async Task<int> CountCriticalNewAsync(CancellationToken ct = default) =>
        await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .CountAsync(a => a.Severity == AlertSeverity.Critical
                          && a.Status   == AlertStatus.New, ct);

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
