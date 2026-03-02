using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Application.Watchdog;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

// ═══════════════════════════════════════════════════════════════════════════
// Kontrakt (komplettering av befintlig partial-deklaration i AlertService.cs)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Fullständigt gränssnitt för IAlertService — kompletterar den befintliga
/// deklarationen i <c>AlertService.cs</c> med alla metoder som controllers behöver.
/// </summary>
public interface IAlertService
{
    Task<AlertListResponse> ListAlertsAsync(
        int page, int pageSize,
        string? statusFilter, string? severityFilter, string? sourceFilter,
        CancellationToken ct = default);

    Task<AlertSummaryDto>  GetSummaryAsync(CancellationToken ct = default);
    Task<WatchdogAlert?>   GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<WatchdogAlert?>   UpdateStatusAsync(
        Guid id, string newStatus, string? comment,
        string updatedBy, CancellationToken ct = default);

    Task<bool>             CreateFromFindingAsync(
        WatchdogFinding finding, CancellationToken ct = default);

    Task<int>              BulkDismissInfoAlertsAsync(
        int olderThanDays, string by, CancellationToken ct = default);

    Task<int>              CountNewAlertsAsync(
        WatchdogSource source, int hours, CancellationToken ct = default);

    Task<int>              CountCriticalNewAsync(CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// AlertServiceImpl  — komplett implementation
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Implementation av <see cref="IAlertService"/>.
///
/// <para>Registreras som Scoped. Kör på <c>IgnoreQueryFilters()</c> eftersom
/// watchdog-alerts inte är tenant-isolerade — de tillhör hela plattformen.</para>
/// </summary>
public sealed class AlertServiceImpl : IAlertService
{
    private readonly SynthtaxDbContext        _db;
    private readonly IAdminAlertPublisher     _publisher;
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

    // ── Läsa ──────────────────────────────────────────────────────────────

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

        var total     = await query.CountAsync(ct);
        var newCount  = await query.CountAsync(a => a.Status == AlertStatus.New, ct);
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

    // ── Skriva ────────────────────────────────────────────────────────────

    public async Task<bool> CreateFromFindingAsync(
        WatchdogFinding finding, CancellationToken ct = default)
    {
        // Idempotens: skapa inte om nyckeln redan finns
        var exists = await _db.WatchdogAlerts
            .IgnoreQueryFilters()
            .AnyAsync(a => a.Source == finding.Source
                        && a.ExternalVersionKey == finding.ExternalVersionKey, ct);

        if (exists)
        {
            _logger.LogDebug(
                "Alert already exists for {Source}/{Key} — skipping.",
                finding.Source, finding.ExternalVersionKey);
            return false;
        }

        var alert = new WatchdogAlert
        {
            Id                   = Guid.NewGuid(),
            Source               = finding.Source,
            Severity             = finding.Severity,
            Status               = AlertStatus.New,
            ExternalVersionKey   = finding.ExternalVersionKey,
            Title                = finding.Title,
            Description          = finding.Description,
            ReleaseNotesUrl      = finding.ReleaseNotesUrl,
            ActionRequired       = finding.ActionRequired,
            ExternalPublishedAt  = finding.ExternalPublishedAt,
            RawPayloadJson       = finding.RawPayloadJson
        };

        _db.WatchdogAlerts.Add(alert);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "WatchdogAlert created: [{Sev}] {Title} ({Key})",
            finding.Severity, finding.Title, finding.ExternalVersionKey);

        // Push till admin-dashboarden via SignalR
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

        _logger.LogInformation(
            "Alert {Id} status → {Status} by {By}", id, newStatus, updatedBy);

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

// ═══════════════════════════════════════════════════════════════════════════
// EF DbContext-tillägg (partial-klass-mönster)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Fas 9-tillägg till SynthtaxDbContext.
/// Lägg till dessa DbSet-properties och OnModelCreating-konfigurationer
/// i befintlig SynthtaxDbContext.cs.
///
/// <code>
/// // DbSets (lägg till i SynthtaxDbContext):
/// public DbSet&lt;WatchdogAlert&gt;   WatchdogAlerts   { get; set; }
/// public DbSet&lt;PluginTelemetry&gt; PluginTelemetry  { get; set; }
/// public DbSet&lt;WatchdogRun&gt;     WatchdogRuns     { get; set; }
/// </code>
/// </summary>
internal static class Fas9DbContextGuide
{
    /// <summary>
    /// Konfiguration att lägga till i OnModelCreating(ModelBuilder mb):
    ///
    /// <code>
    /// // WatchdogAlert
    /// mb.Entity&lt;WatchdogAlert&gt;(b => {
    ///     b.HasIndex(a => new { a.Source, a.ExternalVersionKey }).IsUnique();
    ///     b.HasIndex(a => a.Status);
    ///     b.HasIndex(a => a.CreatedAt);
    ///     b.Property(a => a.Source).HasConversion&lt;int&gt;();
    ///     b.Property(a => a.Severity).HasConversion&lt;int&gt;();
    ///     b.Property(a => a.Status).HasConversion&lt;int&gt;();
    ///     b.Property(a => a.Description).HasMaxLength(4000);
    ///     b.Property(a => a.Title).HasMaxLength(500);
    ///     b.Property(a => a.ExternalVersionKey).HasMaxLength(200);
    /// });
    ///
    /// // PluginTelemetry
    /// mb.Entity&lt;PluginTelemetry&gt;(b => {
    ///     b.HasIndex(a => a.InstallationId);
    ///     b.HasIndex(a => a.PeriodEnd);
    ///     b.HasIndex(a => a.PluginVersion);
    ///     b.HasIndex(a => a.VsVersionBucket);
    ///     b.Property(a => a.PluginVersion).HasMaxLength(20);
    ///     b.Property(a => a.VsVersionBucket).HasMaxLength(10);
    ///     b.Property(a => a.OsPlatform).HasMaxLength(50);
    /// });
    ///
    /// // WatchdogRun
    /// mb.Entity&lt;WatchdogRun&gt;(b => {
    ///     b.HasIndex(r => new { r.Source, r.RanAt });
    ///     b.Property(r => r.Source).HasConversion&lt;int&gt;();
    ///     b.Property(r => r.ErrorMessage).HasMaxLength(1000);
    /// });
    /// </code>
    /// </summary>
    internal static void ModelBuilderGuide() { }
}
