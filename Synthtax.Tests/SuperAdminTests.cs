// Synthtax.Tests/Fas9/SuperAdminTests.cs
// Requires: xunit, FluentAssertions

using FluentAssertions;
using NSubstitute;

namespace Synthtax.Tests;

// ════════════════════════════════════════════════════════════════════════
// Simulerade domäntyper
// ════════════════════════════════════════════════════════════════════════

file enum SubscriptionPlan { Free = 0, Starter = 1, Professional = 2, Enterprise = 99 }
file enum WatchdogSource   { VisualStudio, AiModelClaude, AiModelOpenAi, NuGetPackage, RoslynCompiler, GitHubCopilot, Custom }
file enum AlertSeverity    { Info, Warning, Critical }
file enum AlertStatus      { New, Acknowledged, Resolved, Dismissed }

// ════════════════════════════════════════════════════════════════════════
// OrgAdminDto – validering
// ════════════════════════════════════════════════════════════════════════

file sealed record OrgAdminDto
{
    public Guid   Id                { get; init; }
    public string Name              { get; init; } = "";
    public string Slug              { get; init; } = "";
    public string Plan              { get; init; } = "";
    public int    PurchasedLicenses { get; init; }
    public int    ActiveMembers     { get; init; }
    public bool   IsActive          { get; init; }
    public bool   IsOnTrial         { get; init; }
    public DateTime? TrialEndsAt    { get; init; }
    public IReadOnlyList<string> EnabledFeatures { get; init; } = [];
}

file sealed record CreateOrgRequest
{
    public required string Name              { get; init; }
    public required string Slug              { get; init; }
    public required string Plan              { get; init; }
    public required int    PurchasedLicenses { get; init; }
    public bool   StartOnTrial { get; init; }
    public IReadOnlyList<string> EnabledFeatures { get; init; } = [];
}

file sealed record UpdateOrgRequest
{
    public string? Plan              { get; init; }
    public int?    PurchasedLicenses { get; init; }
    public bool?   IsActive          { get; init; }
    public IReadOnlyList<string>? EnabledFeatures { get; init; }
}

public class OrgAdminDtoTests
{
    [Fact]
    public void SlugShouldBeLowercase()
    {
        var org = new OrgAdminDto { Slug = "acme-corp" };
        org.Slug.Should().Be(org.Slug.ToLowerInvariant());
    }

    [Fact]
    public void IsOnTrial_TrueWhenTrialEndsAtInFuture()
    {
        var org = new OrgAdminDto { IsOnTrial = true, TrialEndsAt = DateTime.UtcNow.AddDays(5) };
        org.IsOnTrial.Should().BeTrue();
        org.TrialEndsAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public void EnabledFeatures_DefaultsToEmpty()
    {
        new OrgAdminDto().EnabledFeatures.Should().BeEmpty();
    }

    [Fact]
    public void CreateOrgRequest_PlanParsing()
    {
        var plans = new[] { "Free", "Starter", "Professional", "Enterprise" };
        foreach (var p in plans)
        {
            var parsed = Enum.TryParse<SubscriptionPlan>(p, ignoreCase: true, out var result);
            parsed.Should().BeTrue($"Plan '{p}' should parse");
        }
        Enum.TryParse<SubscriptionPlan>("Unknown", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(int.MaxValue)]
    public void PurchasedLicenses_CanBePositive(int count)
    {
        var req = new CreateOrgRequest
        {
            Name              = "Test",
            Slug              = "test",
            Plan              = "Professional",
            PurchasedLicenses = count
        };
        req.PurchasedLicenses.Should().Be(count);
    }
}

// ════════════════════════════════════════════════════════════════════════
// Slug-validering
// ════════════════════════════════════════════════════════════════════════

file static class SlugValidator
{
    public static bool IsValid(string slug) =>
        !string.IsNullOrWhiteSpace(slug) &&
        slug.Length <= 50 &&
        slug == slug.ToLowerInvariant() &&
        slug.All(c => char.IsLetterOrDigit(c) || c == '-') &&
        !slug.StartsWith('-') && !slug.EndsWith('-');
}

public class SlugValidatorTests
{
    [Theory]
    [InlineData("acme-corp",    true)]
    [InlineData("my-company",   true)]
    [InlineData("test123",      true)]
    [InlineData("UPPER",        false)]
    [InlineData("-starts",      false)]
    [InlineData("ends-",        false)]
    [InlineData("has space",    false)]
    [InlineData("",             false)]
    [InlineData("a",            true)]
    public void IsValid_CorrectlyValidates(string slug, bool expected)
    {
        SlugValidator.IsValid(slug).Should().Be(expected);
    }

    [Fact]
    public void LongSlug_IsInvalid()
    {
        var longSlug = new string('a', 51);
        SlugValidator.IsValid(longSlug).Should().BeFalse();
    }
}

// ════════════════════════════════════════════════════════════════════════
// WatchdogFinding – modell
// ════════════════════════════════════════════════════════════════════════

file sealed record WatchdogFinding
{
    public required WatchdogSource Source             { get; init; }
    public required AlertSeverity  Severity           { get; init; }
    public required string         ExternalVersionKey { get; init; }
    public required string         Title              { get; init; }
    public required string         Description        { get; init; }
    public string? ReleaseNotesUrl { get; init; }
    public string? ActionRequired  { get; init; }
    public required DateTime ExternalPublishedAt { get; init; }
}

public class WatchdogFindingTests
{
    [Fact]
    public void ExternalVersionKey_IsRequired()
    {
        var finding = new WatchdogFinding
        {
            Source             = WatchdogSource.VisualStudio,
            Severity           = AlertSeverity.Warning,
            ExternalVersionKey = "17.11.0",
            Title              = "VS 17.11 released",
            Description        = "...",
            ExternalPublishedAt = DateTime.UtcNow
        };
        finding.ExternalVersionKey.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("17.11.0", null, AlertSeverity.Warning)] // Minor → Warning
    [InlineData("18.0.0",  "17.10.0", AlertSeverity.Critical)] // Major → Critical
    public void VsSeverity_BasedOnVersionBump(string newVer, string? oldVer, AlertSeverity expected)
    {
        var severity = DetermineVsSeverity(newVer, oldVer);
        severity.Should().Be(expected);
    }

    private static AlertSeverity DetermineVsSeverity(string newVer, string? oldVer)
    {
        if (!TryParseMajorMinor(newVer, out var nMaj, out _)) return AlertSeverity.Warning;
        if (oldVer is null || !TryParseMajorMinor(oldVer, out var oMaj, out _)) return AlertSeverity.Info;
        return nMaj > oMaj ? AlertSeverity.Critical : AlertSeverity.Warning;
    }

    private static bool TryParseMajorMinor(string v, out int maj, out int min)
    {
        maj = 0; min = 0;
        var p = v.Split('.');
        return p.Length >= 2 && int.TryParse(p[0], out maj) && int.TryParse(p[1], out min);
    }
}

// ════════════════════════════════════════════════════════════════════════
// Idempotens – VersionKey
// ════════════════════════════════════════════════════════════════════════

file sealed class InMemoryAlertStore
{
    private readonly HashSet<(WatchdogSource, string)> _keys = new();
    private readonly List<WatchdogFinding> _alerts = [];

    public bool TryAdd(WatchdogFinding finding)
    {
        var key = (finding.Source, finding.ExternalVersionKey);
        if (!_keys.Add(key)) return false;
        _alerts.Add(finding);
        return true;
    }

    public int Count => _alerts.Count;
}

public class WatchdogIdempotencyTests
{
    [Fact]
    public void SameSourceAndVersion_CreatesOnlyOneAlert()
    {
        var store   = new InMemoryAlertStore();
        var finding = MakeFinding("17.11.0", WatchdogSource.VisualStudio);

        store.TryAdd(finding).Should().BeTrue("first time should succeed");
        store.TryAdd(finding).Should().BeFalse("same key should be rejected");
        store.Count.Should().Be(1);
    }

    [Fact]
    public void DifferentVersions_CreateSeparateAlerts()
    {
        var store = new InMemoryAlertStore();
        store.TryAdd(MakeFinding("17.11.0", WatchdogSource.VisualStudio));
        store.TryAdd(MakeFinding("17.12.0", WatchdogSource.VisualStudio));
        store.Count.Should().Be(2);
    }

    [Fact]
    public void SameVersion_DifferentSources_CreateSeparateAlerts()
    {
        var store = new InMemoryAlertStore();
        store.TryAdd(MakeFinding("4.8.0", WatchdogSource.NuGetPackage));
        store.TryAdd(MakeFinding("4.8.0", WatchdogSource.RoslynCompiler));
        store.Count.Should().Be(2);
    }

    private static WatchdogFinding MakeFinding(string ver, WatchdogSource src) => new()
    {
        Source             = src,
        Severity           = AlertSeverity.Warning,
        ExternalVersionKey = ver,
        Title              = $"Test {ver}",
        Description        = "...",
        ExternalPublishedAt = DateTime.UtcNow
    };
}

// ════════════════════════════════════════════════════════════════════════
// AlertStatus – transition-regler
// ════════════════════════════════════════════════════════════════════════

file static class AlertTransitions
{
    // Tillåtna övergångar
    private static readonly HashSet<(AlertStatus From, AlertStatus To)> Allowed =
    [
        (AlertStatus.New, AlertStatus.Acknowledged),
        (AlertStatus.New, AlertStatus.Dismissed),
        (AlertStatus.Acknowledged, AlertStatus.Resolved),
        (AlertStatus.Acknowledged, AlertStatus.Dismissed),
        (AlertStatus.Resolved, AlertStatus.Acknowledged), // Re-open
    ];

    public static bool IsAllowed(AlertStatus from, AlertStatus to) =>
        Allowed.Contains((from, to));
}

public class AlertStatusTransitionTests
{
    [Theory]
    [InlineData(AlertStatus.New,          AlertStatus.Acknowledged, true)]
    [InlineData(AlertStatus.New,          AlertStatus.Dismissed,    true)]
    [InlineData(AlertStatus.Acknowledged, AlertStatus.Resolved,     true)]
    [InlineData(AlertStatus.Acknowledged, AlertStatus.Dismissed,    true)]
    [InlineData(AlertStatus.Resolved,     AlertStatus.Acknowledged, true)]
    [InlineData(AlertStatus.New,          AlertStatus.Resolved,     false)] // Inte tillåtet
    [InlineData(AlertStatus.Dismissed,    AlertStatus.New,          false)] // Inte tillåtet
    public void Transition_Allowed_AsExpected(AlertStatus from, AlertStatus to, bool expected)
    {
        AlertTransitions.IsAllowed(from, to).Should().Be(expected);
    }
}

// ════════════════════════════════════════════════════════════════════════
// Telemetri – sanitering och validering
// ════════════════════════════════════════════════════════════════════════

file static class TelemetryValidator
{
    public static bool IsValid(string pluginVer, string vsVer, string os,
        double latencyMs, double p95, int failed, int total, double signalR)
    {
        if (string.IsNullOrWhiteSpace(pluginVer)) return false;
        if (latencyMs < 0 || p95 < 0)            return false;
        if (failed < 0 || total < 0)              return false;
        if (failed > total)                       return false;
        if (signalR is < 0 or > 1)               return false;
        return true;
    }

    public static string SanitizeVersion(string v)
    {
        var clean = new string(v.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return clean.Length > 20 ? clean[..20] : clean.Length == 0 ? "0.0.0" : clean;
    }

    public static string SanitizeVsVersion(string v)
    {
        var parts = v.Split('.');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var major) &&
            int.TryParse(parts[1], out var minor))
            return $"{major}.{minor}";
        return "unknown";
    }

    public static string SanitizeOs(string os)
    {
        var known = new[] { "Windows 11", "Windows 10", "Windows Server", "macOS", "Linux" };
        foreach (var k in known)
            if (os.StartsWith(k, StringComparison.OrdinalIgnoreCase)) return k;
        return "Other";
    }
}

public class TelemetryValidationTests
{
    [Fact]
    public void ValidRequest_PassesValidation()
    {
        TelemetryValidator.IsValid("1.2.3", "17.10", "Windows 11",
            120.5, 380.0, 2, 100, 0.98).Should().BeTrue();
    }

    [Fact]
    public void FailedGreaterThanTotal_FailsValidation()
    {
        TelemetryValidator.IsValid("1.0.0", "17.10", "Windows 11",
            0, 0, 10, 5, 1.0).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1.0, 100, true)]  // Negativ latens
    [InlineData(100, -1.0, true)]  // Negativ P95
    public void NegativeLatency_FailsValidation(double median, double p95, bool expectFail)
    {
        var valid = TelemetryValidator.IsValid("1.0.0", "17.10", "Win",
            median, p95, 0, 10, 0.5);
        if (expectFail) valid.Should().BeFalse();
    }

    [Theory]
    [InlineData(-0.1, false)]
    [InlineData(0.0,  true)]
    [InlineData(0.5,  true)]
    [InlineData(1.0,  true)]
    [InlineData(1.1,  false)]
    public void SignalRUptime_ValidatedCorrectly(double value, bool expected)
    {
        TelemetryValidator.IsValid("1.0.0", "17.10", "Win", 100, 200, 0, 10, value)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("1.2.3",        "1.2.3")]
    [InlineData("1.2.3-beta",   "1.2.3")]   // Strippar suffix
    [InlineData("abc",          "0.0.0")]   // Okänt → default
    [InlineData("",             "0.0.0")]
    [InlineData("99.99.99.99",  "99.99.99.99")]
    public void SanitizeVersion_NormalizesCorrectly(string input, string expected)
    {
        TelemetryValidator.SanitizeVersion(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("17.10.1",  "17.10")]
    [InlineData("17.9",     "17.9")]
    [InlineData("garbage",  "unknown")]
    [InlineData("18.0.0",   "18.0")]
    public void SanitizeVsVersion_ReducesToMajorMinor(string input, string expected)
    {
        TelemetryValidator.SanitizeVsVersion(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Windows 11",       "Windows 11")]
    [InlineData("Windows 11 Pro",   "Windows 11")]  // Prefix-match
    [InlineData("Windows 10.0.19",  "Windows 10")]
    [InlineData("macOS 14.2",       "macOS")]
    [InlineData("Ubuntu 22.04",     "Other")]
    [InlineData("Android",          "Other")]
    public void SanitizeOs_NormalizesCorrectly(string input, string expected)
    {
        TelemetryValidator.SanitizeOs(input).Should().Be(expected);
    }
}

// ════════════════════════════════════════════════════════════════════════
// GlobalHealthDto – aggregerad statistik
// ════════════════════════════════════════════════════════════════════════

file sealed record GlobalHealthDto
{
    public int    ActiveInstallations    { get; init; }
    public double AvgMedianLatencyMs     { get; init; }
    public double AvgP95LatencyMs        { get; init; }
    public double HealthyInstallFraction { get; init; }
    public int    TotalAnalyzerCrashes   { get; init; }
    public double AvgSignalRUptime       { get; init; }
    public IReadOnlyDictionary<string, int> VersionDistribution  { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> VsVersionDistribution { get; init; } = new Dictionary<string, int>();
}

public class GlobalHealthDtoTests
{
    [Fact]
    public void HealthyInstallFraction_IsInRange()
    {
        var dto = new GlobalHealthDto { HealthyInstallFraction = 0.92 };
        dto.HealthyInstallFraction.Should().BeInRange(0, 1);
    }

    [Fact]
    public void VersionDistribution_AllCountsPositive()
    {
        var dto = new GlobalHealthDto
        {
            VersionDistribution = new Dictionary<string, int>
            {
                ["1.2.0"] = 145,
                ["1.1.9"] = 23
            }
        };
        dto.VersionDistribution.Values.Should().OnlyContain(v => v >= 0);
    }

    [Fact]
    public void ActiveInstallations_NonNegative()
    {
        new GlobalHealthDto { ActiveInstallations = 0 }.ActiveInstallations.Should().BeGreaterThanOrEqualTo(0);
    }
}

// ════════════════════════════════════════════════════════════════════════
// Percentil-beräkning – medianen och P95
// ════════════════════════════════════════════════════════════════════════

file static class PercentileCalculator
{
    public static (double Median, double P95) Calculate(double[] values)
    {
        if (values.Length == 0) return (0, 0);
        var sorted = values.OrderBy(v => v).ToArray();
        var median = sorted[sorted.Length / 2];
        var p95Idx = (int)Math.Floor(sorted.Length * 0.95);
        var p95    = sorted[Math.Min(p95Idx, sorted.Length - 1)];
        return (Math.Round(median, 1), Math.Round(p95, 1));
    }
}

public class PercentileCalculatorTests
{
    [Fact]
    public void EmptyArray_ReturnsZeros()
    {
        var (median, p95) = PercentileCalculator.Calculate([]);
        median.Should().Be(0);
        p95.Should().Be(0);
    }

    [Fact]
    public void SingleValue_ReturnsThatValue()
    {
        var (median, p95) = PercentileCalculator.Calculate([150.0]);
        median.Should().Be(150.0);
        p95.Should().Be(150.0);
    }

    [Fact]
    public void P95_IsGreaterThanOrEqualToMedian()
    {
        var values = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
        var (median, p95) = PercentileCalculator.Calculate(values);
        p95.Should().BeGreaterThanOrEqualTo(median);
    }

    [Fact]
    public void KnownValues_CorrectPercentiles()
    {
        // 10 values: 1..10. Median=5, P95=10
        var values = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var (median, _) = PercentileCalculator.Calculate(values);
        median.Should().Be(5);
    }

    [Fact]
    public void LargeOutlier_AffectsP95NotMedian()
    {
        var values = Enumerable.Range(1, 99).Select(i => (double)i)
            .Append(10000.0)  // En stor outlier
            .ToArray();
        var (median, p95) = PercentileCalculator.Calculate(values);
        median.Should().BeLessThan(100);
        p95.Should().BeGreaterThan(100);
    }
}

// ════════════════════════════════════════════════════════════════════════
// InstallationId persistering
// ════════════════════════════════════════════════════════════════════════

public class InstallationIdTests
{
    [Fact]
    public void TwoNewIds_AreDistinct()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void InstallationId_IsValidGuid()
    {
        var id = Guid.NewGuid();
        id.Should().NotBe(Guid.Empty);
        id.ToString().Should().MatchRegex(
            @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$");
    }
}

// ════════════════════════════════════════════════════════════════════════
// NuGetVersionChecker – logik
// ════════════════════════════════════════════════════════════════════════

file static class NuGetVersionHelper
{
    public static bool IsSignificantUpdate(string? oldVer, string newVer)
    {
        if (oldVer is null) return true;
        if (!System.Version.TryParse(oldVer, out var old)) return true;
        if (!System.Version.TryParse(newVer, out var @new)) return true;
        return @new.Major > old.Major || @new.Minor > old.Minor;
    }
}

public class NuGetVersionHelperTests
{
    [Theory]
    [InlineData(null, "4.9.0", true)]    // Ingen tidigare känd → significant
    [InlineData("4.8.0", "4.9.0", true)] // Minor bump → significant
    [InlineData("4.8.0", "5.0.0", true)] // Major bump → significant
    [InlineData("4.8.0", "4.8.1", false)]// Patch → ej significant
    [InlineData("4.8.2", "4.8.3", false)]// Patch → ej significant
    public void IsSignificantUpdate_CorrectBehavior(
        string? oldVer, string newVer, bool expected)
    {
        NuGetVersionHelper.IsSignificantUpdate(oldVer, newVer).Should().Be(expected);
    }
}

// ════════════════════════════════════════════════════════════════════════
// WatchdogStatusDto – display
// ════════════════════════════════════════════════════════════════════════

file sealed record WatchdogStatusDto
{
    public string   Source        { get; init; } = "";
    public bool     IsEnabled     { get; init; }
    public DateTime? LastRunAt    { get; init; }
    public bool?    LastRunOk     { get; init; }
    public string?  LastError     { get; init; }
    public int      NewAlertsLast24h { get; init; }
}

public class WatchdogStatusDtoTests
{
    [Fact]
    public void AllSourcesRepresented()
    {
        var sources = Enum.GetValues<WatchdogSource>()
            .Select(s => s.ToString()).ToHashSet();
        sources.Should().Contain("VisualStudio");
        sources.Should().Contain("AiModelClaude");
        sources.Should().Contain("NuGetPackage");
        sources.Should().Contain("RoslynCompiler");
    }

    [Fact]
    public void FailedRun_HasErrorMessage()
    {
        var status = new WatchdogStatusDto
        {
            Source      = "VisualStudio",
            IsEnabled   = true,
            LastRunOk   = false,
            LastError   = "Connection timeout"
        };
        status.LastRunOk.Should().BeFalse();
        status.LastError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SuccessfulRun_HasNoError()
    {
        var status = new WatchdogStatusDto
        {
            Source    = "VisualStudio",
            IsEnabled = true,
            LastRunOk = true,
            LastRunAt = DateTime.UtcNow
        };
        status.LastRunOk.Should().BeTrue();
        status.LastError.Should().BeNull();
    }
}
