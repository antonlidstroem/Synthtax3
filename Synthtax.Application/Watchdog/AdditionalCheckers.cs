using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Entities;

namespace Synthtax.Application.Watchdog;

// ═══════════════════════════════════════════════════════════════════════════
// NuGet Package Checker
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bevakar NuGet-paket som VSIX-projektet beror på för nya versioner.
///
/// <para><b>Bevakade paket (konfigurerbart):</b>
/// <list type="bullet">
///   <item>Microsoft.VisualStudio.SDK</item>
///   <item>Microsoft.CodeAnalysis.CSharp.Workspaces</item>
///   <item>Microsoft.AspNetCore.SignalR.Client</item>
///   <item>CommunityToolkit.Mvvm</item>
/// </list>
/// </para>
/// </summary>
public sealed class NuGetPackageChecker : IWatchdogSourceChecker
{
    private readonly HttpClient _http;
    private readonly ILogger<NuGetPackageChecker> _logger;
    private readonly IReadOnlyList<string> _packages;

    // NuGet V3 JSON API — returnerar lista av versioner
    private const string NuGetApiBase = "https://api.nuget.org/v3-flatcontainer";

    // In-memory senast kända version per paket
    private readonly Dictionary<string, string> _knownVersions = new();

    public WatchdogSource Source        => WatchdogSource.NuGetPackage;
    public TimeSpan       CheckInterval => TimeSpan.FromHours(24);

    public NuGetPackageChecker(
        IHttpClientFactory httpFactory,
        ILogger<NuGetPackageChecker> logger,
        IConfiguration config)
    {
        _http   = httpFactory.CreateClient("WatchdogClient");
        _logger = logger;

        // Paket att bevaka — konfigurerbart via appsettings
        _packages = config
            .GetSection("Watchdog:MonitoredPackages")
            .Get<List<string>>()
            ?? DefaultPackages;
    }

    private static readonly List<string> DefaultPackages =
    [
        "Microsoft.VisualStudio.SDK",
        "Microsoft.CodeAnalysis.CSharp.Workspaces",
        "Microsoft.CodeAnalysis.Analyzers",
        "Microsoft.AspNetCore.SignalR.Client",
        "CommunityToolkit.Mvvm"
    ];

    public async Task<IReadOnlyList<WatchdogFinding>> CheckAsync(CancellationToken ct = default)
    {
        var findings = new List<WatchdogFinding>();

        foreach (var package in _packages)
        {
            ct.ThrowIfCancellationRequested();
            var finding = await CheckPackageAsync(package, ct);
            if (finding is not null) findings.Add(finding);
        }

        return findings;
    }

    private async Task<WatchdogFinding?> CheckPackageAsync(string packageId, CancellationToken ct)
    {
        try
        {
            // NuGet flat container: GET /v3-flatcontainer/{id}/index.json
            var url  = $"{NuGetApiBase}/{packageId.ToLowerInvariant()}/index.json";
            var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json    = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("versions", out var versions)) return null;

            var allVersions = versions.EnumerateArray()
                .Select(v => v.GetString())
                .Where(v => v is not null && !v.Contains('-')) // Skippa pre-release
                .Select(v => (v!, TryParseVersion(v!)))
                .Where(t => t.Item2 is not null)
                .OrderByDescending(t => t.Item2)
                .ToList();

            if (allVersions.Count == 0) return null;

            var (latestStr, _) = allVersions[0];

            // Redan känd?
            if (_knownVersions.TryGetValue(packageId, out var known) && known == latestStr)
                return null;

            var isNewMajorOrMinor = IsSignificantUpdate(known, latestStr);
            if (!isNewMajorOrMinor && known is not null) return null; // Skippa patch-updates

            _knownVersions[packageId] = latestStr;

            return new WatchdogFinding
            {
                Source             = Source,
                Severity           = AlertSeverity.Warning,
                ExternalVersionKey = $"{packageId}:{latestStr}",
                Title              = $"NuGet: {packageId} {latestStr} released",
                Description        = $"Package '{packageId}' has a new version available: {latestStr}. " +
                                     $"Previous tracked version: {known ?? "unknown"}. " +
                                     $"Update the PackageReference in Synthtax.Vsix.csproj.",
                ReleaseNotesUrl    = $"https://www.nuget.org/packages/{packageId}/{latestStr}",
                ActionRequired     = $"Update <PackageReference Include=\"{packageId}\" Version=\"{latestStr}\" /> " +
                                     $"in Synthtax.Vsix.csproj. Run VSIX smoke tests after update.",
                ExternalPublishedAt = DateTime.UtcNow,
                RawPayloadJson     = JsonSerializer.Serialize(new { packageId, latestStr })
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NuGet check failed for package {Package}.", packageId);
            return null;
        }
    }

    private static System.Version? TryParseVersion(string v) =>
        System.Version.TryParse(v, out var parsed) ? parsed : null;

    private static bool IsSignificantUpdate(string? oldVer, string newVer)
    {
        if (oldVer is null) return true;
        if (!System.Version.TryParse(oldVer, out var old)) return true;
        if (!System.Version.TryParse(newVer, out var @new)) return true;
        return @new.Major > old.Major || @new.Minor > old.Minor;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Roslyn SDK Checker
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bevakar Roslyn compiler API-förändringar via GitHub releases.
///
/// <para>Källa: <c>https://api.github.com/repos/dotnet/roslyn/releases</c></para>
///
/// <para><b>Larmregler:</b>
/// Ny Roslyn-version som matchar en major VS SDK-uppgradering → Warning.
/// Breaking changes i release notes → Critical.</para>
/// </summary>
public sealed class RoslynSdkChecker : IWatchdogSourceChecker
{
    private readonly HttpClient _http;
    private readonly ILogger<RoslynSdkChecker> _logger;

    private const string GitHubReleasesUrl =
        "https://api.github.com/repos/dotnet/roslyn/releases?per_page=5";

    private string? _lastKnownTag;

    public WatchdogSource Source        => WatchdogSource.RoslynCompiler;
    public TimeSpan       CheckInterval => TimeSpan.FromHours(12);

    public RoslynSdkChecker(IHttpClientFactory httpFactory, ILogger<RoslynSdkChecker> logger,
        IConfiguration config)
    {
        _http   = httpFactory.CreateClient("WatchdogClient");
        _logger = logger;

        // GitHub kräver User-Agent-header
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Synthtax-Watchdog/1.0");

        // Konfigurerbar GitHub-token för högre rate limit
        var ghToken = config["Watchdog:GitHubToken"];
        if (ghToken is not null)
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ghToken);
    }

    public async Task<IReadOnlyList<WatchdogFinding>> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync(GitHubReleasesUrl, ct);
            if (!resp.IsSuccessStatusCode) return [];

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var releases = doc.RootElement.EnumerateArray()
                .Where(r => !r.TryGetProperty("prerelease", out var pre) || !pre.GetBoolean())
                .ToList();

            if (releases.Count == 0) return [];

            var latest = releases[0];
            var tag    = latest.TryGetProperty("tag_name", out var t) ? t.GetString() : null;

            if (tag is null || tag == _lastKnownTag) return [];

            _lastKnownTag = tag;

            var body        = latest.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var isBreaking  = body.Contains("breaking", StringComparison.OrdinalIgnoreCase)
                           || body.Contains("BREAKING CHANGE", StringComparison.OrdinalIgnoreCase);
            var publishedAt = latest.TryGetProperty("published_at", out var pa)
                           && DateTime.TryParse(pa.GetString(), out var dt) ? dt : DateTime.UtcNow;
            var htmlUrl     = latest.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;

            return
            [
                new WatchdogFinding
                {
                    Source             = Source,
                    Severity           = isBreaking ? AlertSeverity.Critical : AlertSeverity.Warning,
                    ExternalVersionKey = tag,
                    Title              = $"Roslyn {tag} released{(isBreaking ? " — Breaking changes!" : "")}",
                    Description        = $"A new Roslyn release ({tag}) is available. " +
                                         (isBreaking
                                             ? "⚠️ Release notes mention breaking changes — review immediately."
                                             : "Review release notes for API changes relevant to SA001/SA002/SA003 analysis rules."),
                    ReleaseNotesUrl    = htmlUrl,
                    ActionRequired     = isBreaking
                        ? $"URGENT: Review Roslyn {tag} breaking changes. Test all three analysis rules (SA001/SA002/SA003) " +
                          $"against the new compiler. Update Microsoft.CodeAnalysis packages."
                        : $"Update Microsoft.CodeAnalysis.CSharp.Workspaces to {tag.TrimStart('v')} and run analysis rule tests.",
                    ExternalPublishedAt = publishedAt,
                    RawPayloadJson     = latest.GetRawText()[..Math.Min(2000, latest.GetRawText().Length)]
                }
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Roslyn SDK checker failed.");
            return [];
        }
    }
}
