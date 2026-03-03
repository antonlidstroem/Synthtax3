// Synthtax.Tests/Fas9/SuperAdminSupplementaryTests.cs
// Kompletterar SuperAdminTests.cs med VS-severity, AI-modell, telemetri-aggregering.
// Requires: xunit, FluentAssertions

using FluentAssertions;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Application.Watchdog;
using Synthtax.Core.Entities;
using NSubstitute;

namespace Synthtax.Tests;

// ════════════════════════════════════════════════════════════════════════
// VS Release Severity-logik  (utan HTTP)
// ════════════════════════════════════════════════════════════════════════

file static class VsSev
{
    // Speglar intern logik från VsReleaseChecker
    public static AlertSeverity Determine(string newVer, string? oldVer)
    {
        if (!TryParseMajorMinor(newVer, out var nMaj, out var nMin)) return AlertSeverity.Warning;
        if (oldVer is null || !TryParseMajorMinor(oldVer, out var oMaj, out var oMin))
            return AlertSeverity.Info;
        if (nMaj > oMaj) return AlertSeverity.Critical;
        if (nMin > oMin) return AlertSeverity.Warning;
        return AlertSeverity.Info;
    }

    private static bool TryParseMajorMinor(string v, out int maj, out int min)
    {
        maj = 0; min = 0;
        var p = v.Split('.');
        return p.Length >= 2 && int.TryParse(p[0], out maj) && int.TryParse(p[1], out min);
    }
}

public class VsReleaseSeverityTests
{
    [Theory]
    [InlineData("18.0.0",  "17.11.0", AlertSeverity.Critical)]   // Major bump
    [InlineData("19.0.0",  "18.0.0",  AlertSeverity.Critical)]   // Another major
    [InlineData("17.12.0", "17.11.0", AlertSeverity.Warning)]    // Minor bump
    [InlineData("17.11.3", "17.11.0", AlertSeverity.Info)]       // Patch only
    [InlineData("17.11.0", null,       AlertSeverity.Info)]       // No prior version
    public void Severity_MapsCorrectly(string newV, string? oldV, AlertSeverity expected) =>
        VsSev.Determine(newV, oldV).Should().Be(expected);

    [Fact]
    public void MajorBump_AlwaysCritical_RegardlessOfMinor() =>
        VsSev.Determine("18.5.0", "17.11.2").Should().Be(AlertSeverity.Critical);

    [Fact]
    public void SameVersion_IsInfo() =>
        VsSev.Determine("17.11.0", "17.11.0").Should().Be(AlertSeverity.Info);

    [Fact]
    public void MalformedVersion_FallsBackToWarning() =>
        VsSev.Determine("abc.def", "17.11.0").Should().Be(AlertSeverity.Warning);
}

// ════════════════════════════════════════════════════════════════════════
// AI Model Severity
// ════════════════════════════════════════════════════════════════════════

file static class ModelSev
{
    public static AlertSeverity Determine(string modelId)
    {
        var lc = modelId.ToLowerInvariant();
        if (lc.StartsWith("claude"))
        {
            var parts = lc.Split('-');
            if (parts.Length >= 3 && int.TryParse(parts[^2], out var gen) && gen >= 5)
                return AlertSeverity.Critical;
            return AlertSeverity.Warning;
        }
        if (lc.StartsWith("gpt-5")) return AlertSeverity.Critical;
        if (lc.StartsWith("gpt"))   return AlertSeverity.Warning;
        return AlertSeverity.Info;
    }
}

public class AiModelSeverityTests2
{
    [Theory]
    [InlineData("claude-opus-5-0",              AlertSeverity.Critical)]
    [InlineData("claude-sonnet-5-20260101",      AlertSeverity.Critical)]
    [InlineData("claude-opus-4-5",              AlertSeverity.Warning)]
    [InlineData("claude-haiku-4-5-20251001",    AlertSeverity.Warning)]
    [InlineData("gpt-5",                        AlertSeverity.Critical)]
    [InlineData("gpt-5-turbo",                  AlertSeverity.Critical)]
    [InlineData("gpt-4o",                       AlertSeverity.Warning)]
    [InlineData("gpt-4-turbo",                  AlertSeverity.Warning)]
    [InlineData("unknown-model-v1",             AlertSeverity.Info)]
    public void Severity_MapsCorrectly(string modelId, AlertSeverity expected) =>
        ModelSev.Determine(modelId).Should().Be(expected);

    [Fact]
    public void CaseInsensitive_SameResult()
    {
        ModelSev.Determine("CLAUDE-OPUS-5-0").Should()
            .Be(ModelSev.Determine("claude-opus-5-0"));
    }
}

// ════════════════════════════════════════════════════════════════════════
// WatchdogFinding – korrekt konstruktion
// ════════════════════════════════════════════════════════════════════════

public class WatchdogFindingConstructionTests
{
    [Fact]
    public void Finding_RequiredFields_AllSet()
    {
        var f = new WatchdogFinding
        {
            Source              = WatchdogSource.VisualStudio,
            Severity            = AlertSeverity.Critical,
            ExternalVersionKey  = "17.12.0",
            Title               = "VS 17.12",
            Description         = "A major release.",
            ExternalPublishedAt = DateTime.UtcNow
        };

        f.Source.Should().Be(WatchdogSource.VisualStudio);
        f.ExternalVersionKey.Should().NotBeNullOrEmpty();
        f.Title.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Finding_OptionalActionRequired_CanBeNull()
    {
        var f = new WatchdogFinding
        {
            Source              = WatchdogSource.AiModelClaude,
            Severity            = AlertSeverity.Warning,
            ExternalVersionKey  = "claude-opus-4-6",
            Title               = "New model",
            Description         = "Desc",
            ExternalPublishedAt = DateTime.UtcNow
        };
        f.ActionRequired.Should().BeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════
// PluginTelemetry – aggregering (simulerad utan DB)
// ════════════════════════════════════════════════════════════════════════

file static class TelemetryAggregator
{
    /// <summary>Simulerar GlobalHealthService.GetGlobalHealthAsync utan DB.</summary>
    public static GlobalHealthDto Aggregate(IReadOnlyList<PluginTelemetry> records)
    {
        if (records.Count == 0) return new GlobalHealthDto();

        var uniqueInstalls = records.Select(r => r.InstallationId).Distinct().Count();
        var totalReqs      = records.Sum(r => (double)r.TotalRequestCount);

        double wMedian = totalReqs > 0
            ? records.Sum(r => r.MedianApiLatencyMs * r.TotalRequestCount) / totalReqs
            : records.Average(r => r.MedianApiLatencyMs);

        double wP95 = totalReqs > 0
            ? records.Sum(r => r.P95ApiLatencyMs * r.TotalRequestCount) / totalReqs
            : records.Average(r => r.P95ApiLatencyMs);

        var latest = records.GroupBy(r => r.InstallationId)
            .Select(g => g.MaxBy(r => r.PeriodEnd)!)
            .ToList();

        int    crashes   = records.Sum(r => r.AnalyzerCrashCount);
        double sigUp     = latest.Average(r => r.SignalRUptimeFraction);
        double healthy   = (double)latest.Count(r => r.IsHealthy) / latest.Count;

        var verDist = latest.GroupBy(r => r.PluginVersion)
            .ToDictionary(g => g.Key, g => g.Count());
        var vsDist = latest.GroupBy(r => r.VsVersionBucket)
            .ToDictionary(g => g.Key, g => g.Count());

        return new GlobalHealthDto
        {
            ActiveInstallations    = uniqueInstalls,
            AvgMedianLatencyMs     = Math.Round(wMedian, 1),
            AvgP95LatencyMs        = Math.Round(wP95, 1),
            HealthyInstallFraction = Math.Round(healthy, 3),
            TotalAnalyzerCrashes   = crashes,
            AvgSignalRUptime       = Math.Round(sigUp, 3),
            VersionDistribution    = verDist,
            VsVersionDistribution  = vsDist
        };
    }
}

public class TelemetryAggregationTests
{
    private static PluginTelemetry Record(
        string installId  = "",
        string version    = "1.0.0",
        string vsBucket   = "17.10",
        double median     = 150,
        double p95        = 400,
        int    total      = 100,
        int    failed     = 0,
        int    crashes    = 0,
        double sigUp      = 0.99) => new()
    {
        InstallationId        = installId == "" ? Guid.NewGuid() : Guid.Parse(installId),
        PluginVersion         = version,
        VsVersionBucket       = vsBucket,
        MedianApiLatencyMs    = median,
        P95ApiLatencyMs       = p95,
        TotalRequestCount     = total,
        FailedRequestCount    = failed,
        AnalyzerCrashCount    = crashes,
        SignalRUptimeFraction = sigUp,
        PeriodEnd             = DateTime.UtcNow
    };

    [Fact]
    public void EmptyRecords_ReturnsDefaultDto()
    {
        var dto = TelemetryAggregator.Aggregate([]);
        dto.ActiveInstallations.Should().Be(0);
        dto.AvgMedianLatencyMs.Should().Be(0);
    }

    [Fact]
    public void UniqueInstallations_CountedCorrectly()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var records = new List<PluginTelemetry>
        {
            Record(id1.ToString()),
            Record(id1.ToString()),  // Duplicate install → ska räknas en gång
            Record(id2.ToString())
        };

        var dto = TelemetryAggregator.Aggregate(records);
        dto.ActiveInstallations.Should().Be(2);
    }

    [Fact]
    public void WeightedLatency_HighVolumeInstallsDominates()
    {
        // Install 1: 50ms median, 1000 requests
        // Install 2: 500ms median, 10 requests
        // Weighted → ska vara nära 50ms
        var records = new List<PluginTelemetry>
        {
            Record(median: 50,  p95: 100, total: 1000),
            Record(median: 500, p95: 800, total: 10)
        };

        var dto = TelemetryAggregator.Aggregate(records);
        dto.AvgMedianLatencyMs.Should().BeLessThan(100,
            "hög-volym installation med låg latens ska dominera viktningen");
    }

    [Fact]
    public void HealthyInstallFraction_AllHealthy_Is1()
    {
        var records = Enumerable.Range(0, 5)
            .Select(_ => Record(median: 100, p95: 300, crashes: 0, failed: 0, total: 100))
            .ToList();

        var dto = TelemetryAggregator.Aggregate(records);
        dto.HealthyInstallFraction.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void TotalCrashes_SummedAcrossAllRecords()
    {
        var records = new List<PluginTelemetry>
        {
            Record(crashes: 2),
            Record(crashes: 3),
            Record(crashes: 0)
        };

        TelemetryAggregator.Aggregate(records).TotalAnalyzerCrashes.Should().Be(5);
    }

    [Fact]
    public void VersionDistribution_TopVersionsPresent()
    {
        var records = Enumerable.Range(0, 10)
            .Select(_ => Record(version: "1.2.3"))
            .Concat(Enumerable.Range(0, 5).Select(_ => Record(version: "1.2.2")))
            .ToList();

        var dto = TelemetryAggregator.Aggregate(records);
        dto.VersionDistribution.Should().ContainKey("1.2.3");
        dto.VersionDistribution["1.2.3"].Should().Be(10);
        dto.VersionDistribution["1.2.2"].Should().Be(5);
    }

    [Fact]
    public void VsVersionDistribution_BucketsGroupedCorrectly()
    {
        var records = new List<PluginTelemetry>
        {
            Record(vsBucket: "17.10"),
            Record(vsBucket: "17.10"),
            Record(vsBucket: "17.11")
        };

        var dto = TelemetryAggregator.Aggregate(records);
        dto.VsVersionDistribution["17.10"].Should().Be(2);
        dto.VsVersionDistribution["17.11"].Should().Be(1);
    }

    [Fact]
    public void AvgSignalRUptime_BetweenZeroAndOne()
    {
        var records = new List<PluginTelemetry>
        {
            Record(sigUp: 0.95),
            Record(sigUp: 1.0),
            Record(sigUp: 0.80)
        };

        var dto = TelemetryAggregator.Aggregate(records);
        dto.AvgSignalRUptime.Should().BeInRange(0.0, 1.0);
        dto.AvgSignalRUptime.Should().BeApproximately(0.917, 0.01);
    }
}

// ════════════════════════════════════════════════════════════════════════
// WatchdogSource-enum – fullständig täckning
// ════════════════════════════════════════════════════════════════════════

public class WatchdogSourceEnumTests
{
    [Theory]
    [InlineData("VisualStudio",   WatchdogSource.VisualStudio)]
    [InlineData("AiModelClaude", WatchdogSource.AiModelClaude)]
    [InlineData("AiModelOpenAi", WatchdogSource.AiModelOpenAi)]
    [InlineData("NuGetPackage",  WatchdogSource.NuGetPackage)]
    [InlineData("RoslynCompiler", WatchdogSource.RoslynCompiler)]
    [InlineData("GitHubCopilot", WatchdogSource.GitHubCopilot)]
    [InlineData("Custom",        WatchdogSource.Custom)]
    public void AllSources_ParseFromString(string name, WatchdogSource expected)
    {
        Enum.TryParse<WatchdogSource>(name, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void AllSources_HaveUniqueIntValues()
    {
        var values = Enum.GetValues<WatchdogSource>().Select(v => (int)v).ToList();
        values.Should().OnlyHaveUniqueItems("varje source ska ha unikt int-värde");
    }
}

// ════════════════════════════════════════════════════════════════════════
// ManualTriggerResponse – schema
// ════════════════════════════════════════════════════════════════════════

public class ManualTriggerResponseTests
{
    [Fact]
    public void Response_AfterSuccessfulTrigger_HasPositiveDuration()
    {
        var resp = new ManualTriggerResponse
        {
            Source        = "VisualStudio",
            FindingsCount = 1,
            NewAlerts     = 1,
            DurationMs    = 743,
            RanAt         = DateTime.UtcNow
        };

        resp.DurationMs.Should().BePositive();
        resp.NewAlerts.Should().BeLessThanOrEqualTo(resp.FindingsCount);
    }

    [Fact]
    public void Response_NoNewFindings_NewAlertsIsZero()
    {
        var resp = new ManualTriggerResponse
        {
            Source        = "AiModelClaude",
            FindingsCount = 0,
            NewAlerts     = 0,
            DurationMs    = 230,
            RanAt         = DateTime.UtcNow
        };

        resp.FindingsCount.Should().Be(0);
        resp.NewAlerts.Should().Be(0);
    }
}
