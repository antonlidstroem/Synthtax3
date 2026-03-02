using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

// ═══════════════════════════════════════════════════════════════════════════
// Kontrakt
// ═══════════════════════════════════════════════════════════════════════════

public interface ITelemetryService
{
    /// <summary>Ta emot och persistera telemetrirapport från ett VSIX-plugin.</summary>
    Task IngestAsync(TelemetryIngestRequest request, CancellationToken ct = default);

    /// <summary>Beräkna aggregerad global hälsa för super-admin-panelen.</summary>
    Task<GlobalHealthDto> GetGlobalHealthAsync(int days = 7, CancellationToken ct = default);

    /// <summary>Ta bort telemetridata äldre än <paramref name="retentionDays"/> dagar.</summary>
    Task<int> PurgeOldDataAsync(int retentionDays = 90, CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// Implementation
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hanterar VSIX-plugin telemetri: ingest, aggregering och retention.
///
/// <para><b>Privacy:</b> Ingen koppling till användare eller organisation —
/// <c>InstallationId</c> är ett anonyomiserat GUID. Ingen IP-adress sparas.</para>
///
/// <para><b>Aggregering:</b>
/// <c>GetGlobalHealthAsync</c> beräknar alla nyckeltal direkt i SQL för effektivitet.
/// Vid hög volym: introducera en nattlig aggregerings-pipeline och cacha resultaten.</para>
/// </summary>
public sealed class TelemetryService : ITelemetryService
{
    private readonly SynthtaxDbContext       _db;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(SynthtaxDbContext db, ILogger<TelemetryService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Ingest ────────────────────────────────────────────────────────────

    public async Task IngestAsync(TelemetryIngestRequest req, CancellationToken ct = default)
    {
        // Validering
        if (req.PeriodEnd <= req.PeriodStart)
            throw new ArgumentException("PeriodEnd must be after PeriodStart.");
        if (req.SignalRUptimeFraction is < 0 or > 1)
            throw new ArgumentException("SignalRUptimeFraction must be in [0, 1].");
        if (req.TotalRequestCount < 0 || req.FailedRequestCount < 0)
            throw new ArgumentException("Request counts must be non-negative.");
        if (req.FailedRequestCount > req.TotalRequestCount)
            throw new ArgumentException("FailedRequestCount cannot exceed TotalRequestCount.");

        // Rundning av perioden till timme (anonymisering av exakt tidpunkt)
        var periodStart = new DateTime(
            req.PeriodStart.Year, req.PeriodStart.Month, req.PeriodStart.Day,
            req.PeriodStart.Hour, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(
            req.PeriodEnd.Year, req.PeriodEnd.Month, req.PeriodEnd.Day,
            req.PeriodEnd.Hour, 0, 0, DateTimeKind.Utc);

        // Deduplicering: samma installation + period → uppdatera istf skapa ny
        var existing = await _db.PluginTelemetry
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t =>
                t.InstallationId == req.InstallationId &&
                t.PeriodStart    == periodStart &&
                t.PeriodEnd      == periodEnd, ct);

        if (existing is not null)
        {
            // Uppdatera befintlig post (plugin kan skicka igen efter retry)
            UpdateRecord(existing, req, periodStart, periodEnd);
        }
        else
        {
            var record = new PluginTelemetry
            {
                Id              = Guid.NewGuid(),
                InstallationId  = req.InstallationId,
                PluginVersion   = SanitizeVersion(req.PluginVersion),
                VsVersionBucket = SanitizeVsVersion(req.VsVersionBucket),
                OsPlatform      = SanitizeOs(req.OsPlatform),
                PeriodStart     = periodStart,
                PeriodEnd       = periodEnd
            };
            UpdateRecord(record, req, periodStart, periodEnd);
            _db.PluginTelemetry.Add(record);
        }

        await _db.SaveChangesAsync(ct);
    }

    // ── Global Health ─────────────────────────────────────────────────────

    public async Task<GlobalHealthDto> GetGlobalHealthAsync(
        int days = 7, CancellationToken ct = default)
    {
        var since    = DateTime.UtcNow.AddDays(-days);
        var baseline = _db.PluginTelemetry
            .IgnoreQueryFilters()
            .Where(t => t.PeriodStart >= since)
            .AsNoTracking();

        // Aggregerade metrics
        var metrics = await baseline
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count         = g.Count(),
                AvgMedian     = g.Average(t => t.MedianApiLatencyMs),
                AvgP95        = g.Average(t => t.P95ApiLatencyMs),
                TotalCrashes  = g.Sum(t => t.AnalyzerCrashCount),
                AvgSignalR    = g.Average(t => t.SignalRUptimeFraction),
                HealthyCount  = g.Count(t =>
                    t.FailedRequestCount == 0 &&
                    t.P95ApiLatencyMs < 2000 &&
                    t.AnalyzerCrashCount == 0)
            })
            .FirstOrDefaultAsync(ct);

        // Version-distribution
        var versionDist = await baseline
            .GroupBy(t => t.PluginVersion)
            .OrderByDescending(g => g.Count())
            .Take(20)
            .Select(g => new { Version = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Version, x => x.Count, ct);

        // VS-version-distribution
        var vsDist = await baseline
            .GroupBy(t => t.VsVersionBucket)
            .OrderByDescending(g => g.Count())
            .Take(15)
            .Select(g => new { VsVersion = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.VsVersion, x => x.Count, ct);

        // Unika aktiva installationer
        var activeInstalls = await baseline
            .Select(t => t.InstallationId)
            .Distinct()
            .CountAsync(ct);

        // Dagliga aktiva (för sparkline): senaste 14 dagar
        var dailyActive = await _db.PluginTelemetry
            .IgnoreQueryFilters()
            .Where(t => t.PeriodStart >= DateTime.UtcNow.AddDays(-14))
            .GroupBy(t => t.PeriodStart.Date)
            .Select(g => new DailyActiveDto(g.Key, g.Select(t => t.InstallationId).Distinct().Count()))
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        var count = metrics?.Count ?? 0;
        var healthyCount = metrics?.HealthyCount ?? 0;

        return new GlobalHealthDto
        {
            ActiveInstallations    = activeInstalls,
            AvgMedianLatencyMs     = metrics?.AvgMedian ?? 0,
            AvgP95LatencyMs        = metrics?.AvgP95    ?? 0,
            HealthyInstallFraction = count == 0 ? 1.0 : (double)healthyCount / count,
            TotalAnalyzerCrashes   = metrics?.TotalCrashes ?? 0,
            AvgSignalRUptime       = metrics?.AvgSignalR   ?? 0,
            VersionDistribution    = versionDist,
            VsVersionDistribution  = vsDist,
            DailyActive            = dailyActive,
            GeneratedAt            = DateTime.UtcNow
        };
    }

    // ── Retention ─────────────────────────────────────────────────────────

    public async Task<int> PurgeOldDataAsync(
        int retentionDays = 90, CancellationToken ct = default)
    {
        var cutoff  = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = await _db.PluginTelemetry
            .IgnoreQueryFilters()
            .Where(t => t.PeriodEnd < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation(
                "Telemetry retention: deleted {Count} records older than {Days} days.",
                deleted, retentionDays);

        return deleted;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privat hjälp
    // ═══════════════════════════════════════════════════════════════════════

    private static void UpdateRecord(
        PluginTelemetry record, TelemetryIngestRequest req,
        DateTime periodStart, DateTime periodEnd)
    {
        record.MedianApiLatencyMs    = Math.Max(0, req.MedianApiLatencyMs);
        record.P95ApiLatencyMs       = Math.Max(0, req.P95ApiLatencyMs);
        record.FailedRequestCount    = Math.Max(0, req.FailedRequestCount);
        record.TotalRequestCount     = Math.Max(0, req.TotalRequestCount);
        record.AnalyzerCrashCount    = Math.Max(0, req.AnalyzerCrashCount);
        record.SignalRUptimeFraction  = Math.Clamp(req.SignalRUptimeFraction, 0, 1);
        record.PeriodStart           = periodStart;
        record.PeriodEnd             = periodEnd;
    }

    // Sanitering: förhindra injection och normaliserar format

    private static string SanitizeVersion(string v)
    {
        // Behåll bara siffror och punkter, max 20 tecken
        var clean = new string(v.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return clean.Length > 20 ? clean[..20] : clean.Length == 0 ? "0.0.0" : clean;
    }

    private static string SanitizeVsVersion(string v)
    {
        // Format: "17.10" — två siffrigt major.minor
        var parts = v.Split('.');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
            return $"{major}.{minor}";
        return "unknown";
    }

    private static string SanitizeOs(string os)
    {
        // Tillåt bara kända prefix, max 30 tecken
        var known = new[] { "Windows 11", "Windows 10", "Windows Server", "macOS", "Linux" };
        foreach (var prefix in known)
            if (os.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return prefix;
        return "Other";
    }
}
