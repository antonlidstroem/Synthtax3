using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Core.Entities;

namespace Synthtax.Application.Watchdog;

// ═══════════════════════════════════════════════════════════════════════════
// Kontrakt
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Gränssnitt som alla watchdog-källcheckers implementerar.
/// <para>Registreras som IEnumerable&lt;IWatchdogSourceChecker&gt; i DI.</para>
/// </summary>
public interface IWatchdogSourceChecker
{
    /// <summary>Källkategorin denna checker ansvarar för.</summary>
    WatchdogSource Source { get; }

    /// <summary>Hur ofta denna källa bör checkas (polling-intervall).</summary>
    TimeSpan CheckInterval { get; }

    /// <summary>
    /// Kör en check mot den externa källan.
    /// Returnerar en lista av nya fynd (tomma = inga förändringar).
    /// </summary>
    Task<IReadOnlyList<WatchdogFinding>> CheckAsync(CancellationToken ct = default);
}

/// <summary>Immutabelt resultat från en enskild watchdog-check.</summary>
public sealed record WatchdogFinding
{
    public required WatchdogSource Source             { get; init; }
    public required AlertSeverity  Severity           { get; init; }
    public required string         ExternalVersionKey { get; init; }
    public required string         Title              { get; init; }
    public required string         Description        { get; init; }
    public          string?        ReleaseNotesUrl    { get; init; }
    public          string?        ActionRequired     { get; init; }
    public required DateTime       ExternalPublishedAt { get; init; }
    public          string?        RawPayloadJson     { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// Visual Studio Release Checker
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bevakar Visual Studios release-kanal via Microsofts officiella JSON-feed.
///
/// <para>Källa: <c>https://aka.ms/vs/releases</c> /
/// <c>https://visualstudio.microsoft.com/services/releaseHistory</c></para>
///
/// <para><b>Larmregler:</b>
/// <list type="bullet">
///   <item>Ny <b>Major</b>-release (18.x): <c>Critical</c> — kräver kompatibilitetstest.</item>
///   <item>Ny <b>Minor</b>-release (17.x): <c>Warning</c> — bör testas mot SDK.</item>
///   <item>Ny <b>Patch</b>-release: <c>Info</c> — loggning utan åtgärd.</item>
/// </list>
/// </para>
/// </summary>
public sealed class VsReleaseChecker : IWatchdogSourceChecker
{
    private readonly HttpClient            _http;
    private readonly ILogger<VsReleaseChecker> _logger;

    // Feed från Visual Studio Release History (JSON)
    private const string FeedUrl =
        "https://aka.ms/vs/releases";

    // Senast kända version (in-memory — persisted av WatchdogBackgroundService)
    private string? _lastKnownVersion;

    public WatchdogSource Source        => WatchdogSource.VisualStudio;
    public TimeSpan       CheckInterval => TimeSpan.FromHours(6);

    public VsReleaseChecker(IHttpClientFactory httpFactory, ILogger<VsReleaseChecker> logger)
    {
        _http   = httpFactory.CreateClient("WatchdogClient");
        _logger = logger;
    }

    public async Task<IReadOnlyList<WatchdogFinding>> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var releases = await FetchReleasesAsync(ct);
            if (releases is null || releases.Count == 0) return [];

            var findings = new List<WatchdogFinding>();
            var latestStable = releases
                .Where(r => r.IsStable)
                .MaxBy(r => r.ParsedVersion);

            if (latestStable is null) return [];

            var latestKey = latestStable.Version;

            // Ny version sedan sist?
            if (_lastKnownVersion == latestKey) return [];

            _logger.LogInformation(
                "VS Release Checker: New version detected: {New} (was {Old})",
                latestKey, _lastKnownVersion ?? "unknown");

            var severity = DetermineVsSeverity(latestKey, _lastKnownVersion);

            findings.Add(new WatchdogFinding
            {
                Source             = Source,
                Severity           = severity,
                ExternalVersionKey = latestKey,
                Title              = $"Visual Studio {latestKey} released",
                Description        = BuildVsDescription(latestStable, severity),
                ReleaseNotesUrl    = latestStable.ReleaseNotesUrl,
                ActionRequired     = BuildVsAction(severity, latestKey),
                ExternalPublishedAt = latestStable.PublishedAt ?? DateTime.UtcNow,
                RawPayloadJson     = JsonSerializer.Serialize(latestStable)
            });

            _lastKnownVersion = latestKey;
            return findings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VS Release Checker failed.");
            return [];
        }
    }

    private async Task<List<VsReleaseEntry>?> FetchReleasesAsync(CancellationToken ct)
    {
        // Microsoft erbjuder JSON-feed via updateservice API
        // Fallback: parse HTML headers om JSON-feed saknas
        try
        {
            var resp = await _http.GetAsync(
                "https://aka.ms/vs/releases/json", ct);

            if (!resp.IsSuccessStatusCode)
            {
                // Prova alternativ feed
                resp = await _http.GetAsync(
                    "https://visualstudio.microsoft.com/services/releaseHistory", ct);
            }

            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            return ParseVsReleaseFeed(json);
        }
        catch
        {
            return null;
        }
    }

    // ── Parsning ──────────────────────────────────────────────────────────

    private static List<VsReleaseEntry> ParseVsReleaseFeed(string json)
    {
        var results = new List<VsReleaseEntry>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Försök navigera JSON-strukturen (varierar beroende på feed-format)
            var releases = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("releases", out var rel) ? rel
                : root.TryGetProperty("channelItems", out var ch) ? ch
                : (JsonElement?)null;

            if (releases is null) return results;

            foreach (var entry in releases.Value.EnumerateArray())
            {
                var version = TryGetString(entry, "version", "versionString", "releaseVersion");
                if (version is null) continue;

                results.Add(new VsReleaseEntry
                {
                    Version          = version,
                    ReleaseNotesUrl  = TryGetString(entry, "releaseNotesUrl", "url"),
                    PublishedAt      = TryGetDate(entry, "publishedDate", "date"),
                    IsStable         = !version.Contains("Preview", StringComparison.OrdinalIgnoreCase)
                                    && !version.Contains("RC")
                });
            }
        }
        catch { /* Returnera partiellt resultat */ }
        return results;
    }

    // ── Severity-logik ────────────────────────────────────────────────────

    private static AlertSeverity DetermineVsSeverity(string newVer, string? oldVer)
    {
        if (!TryParseMajorMinor(newVer, out var newMaj, out var newMin)) return AlertSeverity.Warning;
        if (oldVer is null || !TryParseMajorMinor(oldVer, out var oldMaj, out var oldMin))
            return AlertSeverity.Info;

        if (newMaj > oldMaj)  return AlertSeverity.Critical; // Major upgrade (17→18)
        if (newMin > oldMin)  return AlertSeverity.Warning;  // Minor upgrade (17.10→17.11)
        return AlertSeverity.Info;                            // Patch
    }

    private static string BuildVsDescription(VsReleaseEntry entry, AlertSeverity sev) =>
        sev == AlertSeverity.Critical
            ? $"A new major version of Visual Studio ({entry.Version}) has been released. " +
              $"The Synthtax VSIX must be tested for compatibility with the new VS SDK."
            : $"Visual Studio {entry.Version} is now available. " +
              $"Verify that the Synthtax VSIX works correctly with this release.";

    private static string BuildVsAction(AlertSeverity sev, string version) => sev switch
    {
        AlertSeverity.Critical =>
            $"URGENT: Run full VSIX compatibility test suite against VS {version} SDK. " +
            $"Update PackageReference versions in Synthtax.Vsix.csproj if needed. " +
            $"Release a hotfix before VS {version} reaches 10% user adoption.",
        AlertSeverity.Warning =>
            $"Run VSIX smoke tests against VS {version}. " +
            $"Check for deprecated VS SDK APIs in release notes.",
        _ => $"Monitor VS {version} for any Roslyn API changes that may affect analysis rules."
    };

    // ── JSON-helpers ──────────────────────────────────────────────────────

    private static string? TryGetString(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
            if (el.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        return null;
    }

    private static DateTime? TryGetDate(JsonElement el, params string[] keys)
    {
        foreach (var k in keys)
            if (el.TryGetProperty(k, out var v) &&
                DateTime.TryParse(v.GetString(), out var dt))
                return dt;
        return null;
    }

    private static bool TryParseMajorMinor(string ver, out int major, out int minor)
    {
        major = 0; minor = 0;
        var parts = ver.Split('.');
        return parts.Length >= 2 &&
               int.TryParse(parts[0], out major) &&
               int.TryParse(parts[1], out minor);
    }

    private sealed record VsReleaseEntry
    {
        public string   Version         { get; init; } = "";
        public string?  ReleaseNotesUrl { get; init; }
        public DateTime? PublishedAt    { get; init; }
        public bool     IsStable        { get; init; }
        public Version? ParsedVersion   => System.Version.TryParse(Version, out var v) ? v : null;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// AI Model Changelog Checker
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bevakar Anthropic och OpenAI för nya AI-modeller.
///
/// <para>Källa: Anthropic — <c>https://docs.anthropic.com/en/docs/about-claude/models</c>,
/// OpenAI — <c>https://platform.openai.com/docs/models</c></para>
///
/// <para><b>Larmregler:</b>
/// <list type="bullet">
///   <item>Ny <b>generation</b> (claude-5-x, gpt-5): <c>Critical</c> —
///         PromptFactory-templates kan behöva uppdateras.</item>
///   <item>Ny <b>minor variant</b> (claude-opus-4-7): <c>Warning</c> —
///         validera att promptar fortfarande ger korrekt output.</item>
/// </list>
/// </para>
/// </summary>
public sealed class AiModelChangelogChecker : IWatchdogSourceChecker
{
    private readonly HttpClient _http;
    private readonly ILogger<AiModelChangelogChecker> _logger;

    // Anthropic Models API (officiellt REST-endpoint)
    private const string AnthropicModelsUrl =
        "https://api.anthropic.com/v1/models";

    // OpenAI Models API
    private const string OpenAiModelsUrl =
        "https://api.openai.com/v1/models";

    private readonly HashSet<string> _knownModelIds = new(StringComparer.OrdinalIgnoreCase);

    // Konfigurerbara nycklar (sätts via IConfiguration i DI)
    private readonly string? _anthropicApiKey;
    private readonly string? _openAiApiKey;

    public WatchdogSource Source        => WatchdogSource.AiModelClaude;
    public TimeSpan       CheckInterval => TimeSpan.FromHours(12);

    public AiModelChangelogChecker(
        IHttpClientFactory httpFactory,
        ILogger<AiModelChangelogChecker> logger,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _http          = httpFactory.CreateClient("WatchdogClient");
        _logger        = logger;
        _anthropicApiKey = config["Watchdog:AnthropicApiKey"];
        _openAiApiKey    = config["Watchdog:OpenAiApiKey"];
    }

    public async Task<IReadOnlyList<WatchdogFinding>> CheckAsync(CancellationToken ct = default)
    {
        var findings = new List<WatchdogFinding>();

        // Anthropic-check
        if (_anthropicApiKey is not null)
            findings.AddRange(await CheckAnthropicAsync(ct));

        // OpenAI-check (separat källa men kombineras här för enkelhet)
        if (_openAiApiKey is not null)
            findings.AddRange(await CheckOpenAiAsync(ct));

        return findings;
    }

    // ── Anthropic ─────────────────────────────────────────────────────────

    private async Task<List<WatchdogFinding>> CheckAnthropicAsync(CancellationToken ct)
    {
        var findings = new List<WatchdogFinding>();
        try
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "x-api-key", _anthropicApiKey);
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "anthropic-version", "2023-06-01");

            var resp = await _http.GetAsync(AnthropicModelsUrl, ct);
            if (!resp.IsSuccessStatusCode) return findings;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var models = doc.RootElement.TryGetProperty("data", out var data)
                ? data.EnumerateArray().ToList()
                : [];

            foreach (var model in models)
            {
                var modelId = model.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (modelId is null || _knownModelIds.Contains(modelId)) continue;

                _knownModelIds.Add(modelId);
                var severity = DetermineModelSeverity(modelId, "claude");

                if (severity == AlertSeverity.Info) continue; // Skippa rena info-models

                findings.Add(new WatchdogFinding
                {
                    Source             = WatchdogSource.AiModelClaude,
                    Severity           = severity,
                    ExternalVersionKey = modelId,
                    Title              = $"New Anthropic model available: {modelId}",
                    Description        = BuildModelDescription(modelId, "Anthropic"),
                    ReleaseNotesUrl    = "https://docs.anthropic.com/en/docs/about-claude/models",
                    ActionRequired     = BuildModelAction(severity, modelId),
                    ExternalPublishedAt = DateTime.UtcNow,
                    RawPayloadJson     = model.GetRawText()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Anthropic model check failed.");
        }
        return findings;
    }

    private async Task<List<WatchdogFinding>> CheckOpenAiAsync(CancellationToken ct)
    {
        var findings = new List<WatchdogFinding>();
        try
        {
            _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", _openAiApiKey);

            var resp = await _http.GetAsync(OpenAiModelsUrl, ct);
            if (!resp.IsSuccessStatusCode) return findings;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var models = doc.RootElement.TryGetProperty("data", out var data)
                ? data.EnumerateArray()
                    .Where(m => m.TryGetProperty("id", out var mid) &&
                                (mid.GetString()?.StartsWith("gpt-4") == true ||
                                 mid.GetString()?.StartsWith("gpt-5") == true))
                    .ToList()
                : [];

            foreach (var model in models)
            {
                var modelId = model.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (modelId is null || _knownModelIds.Contains(modelId)) continue;

                _knownModelIds.Add(modelId);
                var severity = DetermineModelSeverity(modelId, "gpt");

                if (severity == AlertSeverity.Info) continue;

                findings.Add(new WatchdogFinding
                {
                    Source             = WatchdogSource.AiModelOpenAi,
                    Severity           = severity,
                    ExternalVersionKey = modelId,
                    Title              = $"New OpenAI model available: {modelId}",
                    Description        = BuildModelDescription(modelId, "OpenAI"),
                    ReleaseNotesUrl    = "https://platform.openai.com/docs/models",
                    ActionRequired     = BuildModelAction(severity, modelId),
                    ExternalPublishedAt = DateTime.UtcNow,
                    RawPayloadJson     = model.GetRawText()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI model check failed.");
        }
        return findings;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static AlertSeverity DetermineModelSeverity(string modelId, string prefix)
    {
        // "claude-opus-5-x" eller "gpt-5" → Critical (ny generation)
        // "claude-sonnet-4-7" → Warning (ny variant i känd generation)
        var lc = modelId.ToLowerInvariant();

        if (prefix == "claude")
        {
            // Extrahera generations-nummer från "claude-xxx-N-..."
            var parts = lc.Split('-');
            if (parts.Length >= 3 && int.TryParse(parts[^2], out var gen) && gen >= 5)
                return AlertSeverity.Critical;
            return AlertSeverity.Warning;
        }

        if (prefix == "gpt")
        {
            if (lc.StartsWith("gpt-5")) return AlertSeverity.Critical;
            return AlertSeverity.Warning;
        }

        return AlertSeverity.Info;
    }

    private static string BuildModelDescription(string modelId, string provider) =>
        $"A new {provider} model has been detected: '{modelId}'. " +
        $"Verify that Synthtax PromptFactory templates produce correct output with this model, " +
        $"and update default model references in configuration if appropriate.";

    private static string BuildModelAction(AlertSeverity sev, string modelId) => sev switch
    {
        AlertSeverity.Critical =>
            $"Test all PromptFactory templates (SA001/SA002/SA003) against {modelId}. " +
            $"Update SynthtaxOptions.DefaultModel if output quality is improved. " +
            $"Run regression tests for Claude Technical Spec format.",
        _ =>
            $"Spot-check PromptFactory output against {modelId}. " +
            $"Consider updating model reference in Synthtax settings."
    };
}
