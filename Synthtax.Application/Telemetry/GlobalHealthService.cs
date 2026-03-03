using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Core.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Application.Telemetry;

// ═══════════════════════════════════════════════════════════════════════════
// Kontrakt
// ═══════════════════════════════════════════════════════════════════════════

public interface IGlobalHealthService
{
    /// <summary>Hämtar aggregerad global hälsoöversikt för super-admin-dashboarden.</summary>
    Task<GlobalHealthDto> GetGlobalHealthAsync(
        int lookbackDays = 7, CancellationToken ct = default);

    /// <summary>Tar emot telemetri-ping från ett installerat plugin.</summary>
    Task IngestAsync(TelemetryIngestRequest request, CancellationToken ct = default);

    /// <summary>Rensar telemetri äldre än retentionDays dagar.</summary>
    Task PurgeOldRecordsAsync(int retentionDays = 90, CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// Implementation
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hanterar anonymiserad telemetri från installerade VSIX-plugins.
///
/// <para><b>Privacy-principer (GDPR):</b>
/// <list type="bullet">
///   <item><c>InstallationId</c> är ett slumpmässigt GUID — inte kopplat till
///         användare, org eller maskin.</item>
///   <item>VS-version och OS rundas av vid ingest (17.10.3 → "17.10").</item>
///   <item>Retention 90 dagar, automatisk rensning via <see cref="PurgeOldRecordsAsync"/>.</item>
///   <item>Inga IP-adresser, inga stacktraces, inga användardata.</item>
/// </list>
/// </para>
///
/// <para><b>Aggregeringsstrategi:</b>
/// Alla queries kör på IgnoreQueryFilters (telemetri är inte tenant-isolerat)
/// och aggregeras på DB-sidan för att minimera minnesfotavtrycket.</para>
/// </summary>
public sealed class GlobalHealthService : IGlobalHealthService
{
    private readonly SynthtaxDbContext        _db;
    private readonly ILogger<GlobalHealthService> _logger;

    // Betrodda VS-versionsbuckets (avvisar uppenbart felaktiga indata)
    private static readonly System.Text.RegularExpressions.Regex VsBucketRegex =
        new(@"^\d{1,2}\.\d{1,2}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Betrodda plugin-versioner (semver)
    private static readonly System.Text.RegularExpressions.Regex SemVerRegex =
        new(@"^\d+\.\d+\.\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public GlobalHealthService(SynthtaxDbContext db, ILogger<GlobalHealthService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ── Ingest ────────────────────────────────────────────────────────────

    public async Task IngestAsync(TelemetryIngestRequest req, CancellationToken ct = default)
    {
        // Validera och sanera indata
        if (!SemVerRegex.IsMatch(req.PluginVersion))
        {
            _logger.LogWarning("Telemetry rejected: invalid PluginVersion '{V}'", req.PluginVersion);
            return;
        }
        if (!VsBucketRegex.IsMatch(req.VsVersionBucket))
        {
            _logger.LogWarning("Telemetry rejected: invalid VsVersionBucket '{V}'", req.VsVersionBucket);
            return;
        }

        // Klipp orimliga latens-värden
        var record = new PluginTelemetry
        {
            Id                    = Guid.NewGuid(),
            InstallationId        = req.InstallationId,
            PluginVersion         = req.PluginVersion,
            VsVersionBucket       = req.VsVersionBucket,
            OsPlatform            = SanitizeOsPlatform(req.OsPlatform),
            MedianApiLatencyMs    = Math.Clamp(req.MedianApiLatencyMs,    0, 60_000),
            P95ApiLatencyMs       = Math.Clamp(req.P95ApiLatencyMs,       0, 60_000),
            FailedRequestCount    = Math.Max(0, req.FailedRequestCount),
            TotalRequestCount     = Math.Max(0, req.TotalRequestCount),
            AnalyzerCrashCount    = Math.Clamp(req.AnalyzerCrashCount,    0, 1_000),
            SignalRUptimeFraction = Math.Clamp(req.SignalRUptimeFraction, 0.0, 1.0),
            PeriodStart           = req.PeriodStart.ToUniversalTime(),
            PeriodEnd             = req.PeriodEnd.ToUniversalTime()
        };

        _db.PluginTelemetry.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "Telemetry ingested: {Id} v={Ver} vs={Vs} latency={Lat}ms",
            req.InstallationId, req.PluginVersion, req.VsVersionBucket,
            req.MedianApiLatencyMs);
    }

    // ── Aggregering ───────────────────────────────────────────────────────

    public async Task<GlobalHealthDto> GetGlobalHealthAsync(
        int lookbackDays = 7, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);

        var records = await _db.PluginTelemetry
            .Where(t => t.PeriodEnd >= cutoff)
            .AsNoTracking()
            .ToListAsync(ct);

        if (records.Count == 0)
            return new GlobalHealthDto { GeneratedAt = DateTime.UtcNow };

        // Unika installationer (per InstallationId)
        var uniqueInstalls = records.Select(r => r.InstallationId).Distinct().Count();

        // Latens-aggregat (viktad per TotalRequestCount)
        var totalReqs = records.Sum(r => (double)r.TotalRequestCount);

        double weightedMedian = totalReqs > 0
            ? records.Sum(r => r.MedianApiLatencyMs * r.TotalRequestCount) / totalReqs
            : records.Average(r => r.MedianApiLatencyMs);

        double weightedP95 = totalReqs > 0
            ? records.Sum(r => r.P95ApiLatencyMs * r.TotalRequestCount) / totalReqs
            : records.Average(r => r.P95ApiLatencyMs);

        // Per installation: senaste record
        var latestPerInstall = records
            .GroupBy(r => r.InstallationId)
            .Select(g => g.MaxBy(r => r.PeriodEnd)!)
            .ToList();

        int    totalCrashes  = records.Sum(r => r.AnalyzerCrashCount);
        double avgSignalRUp  = latestPerInstall.Average(r => r.SignalRUptimeFraction);
        int    healthyCount  = latestPerInstall.Count(r => r.IsHealthy);
        double healthyFrac   = (double)healthyCount / Math.Max(latestPerInstall.Count, 1);

        // Version-distribution (senaste version per installation)
        var versionDist = latestPerInstall
            .GroupBy(r => r.PluginVersion)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        var vsDist = latestPerInstall
            .GroupBy(r => r.VsVersionBucket)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        // Daglig aktiva installationer (senaste 14 dagar)
        var daily = await BuildDailyActiveAsync(14, ct);

        return new GlobalHealthDto
        {
            ActiveInstallations    = uniqueInstalls,
            AvgMedianLatencyMs     = Math.Round(weightedMedian, 1),
            AvgP95LatencyMs        = Math.Round(weightedP95, 1),
            HealthyInstallFraction = Math.Round(healthyFrac, 3),
            TotalAnalyzerCrashes   = totalCrashes,
            AvgSignalRUptime       = Math.Round(avgSignalRUp, 3),
            VersionDistribution    = versionDist,
            VsVersionDistribution  = vsDist,
            DailyActive            = daily,
            GeneratedAt            = DateTime.UtcNow
        };
    }

    // ── Rensning ──────────────────────────────────────────────────────────

    public async Task PurgeOldRecordsAsync(int retentionDays = 90, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = await _db.PluginTelemetry
            .Where(t => t.PeriodEnd < cutoff)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogInformation(
                "Purged {N} telemetry records older than {Days} days.", deleted, retentionDays);
    }

    // ── Privat ────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<DailyActiveDto>> BuildDailyActiveAsync(
        int days, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        // Hämta (date-bucket, count) direkt från DB
        var raw = await _db.PluginTelemetry
            .Where(t => t.PeriodEnd.Date >= cutoff)
            .GroupBy(t => t.PeriodEnd.Date)
            .Select(g => new { Date = g.Key, Count = g.Select(r => r.InstallationId).Distinct().Count() })
            .OrderBy(x => x.Date)
            .AsNoTracking()
            .ToListAsync(ct);

        // Fyll tomma dagar med 0
        var result = new List<DailyActiveDto>();
        for (int i = days; i >= 0; i--)
        {
            var d   = DateTime.UtcNow.Date.AddDays(-i);
            var row = raw.FirstOrDefault(x => x.Date == d);
            result.Add(new DailyActiveDto(d, row?.Count ?? 0));
        }
        return result;
    }

    private static string SanitizeOsPlatform(string os)
    {
        // Tillåt bara kända OS-strängar
        var known = new[] { "Windows 11", "Windows 10", "Windows Server 2022",
                            "Windows Server 2019", "Windows Server 2016" };
        return known.FirstOrDefault(k =>
            os.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? "Windows (other)";
    }
}
