using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Normalization;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Contracts;

/// <summary>
/// Abstrakt basklass för alla language-plugins.
/// Ger standardimplementering av boilerplate: logging, timing,
/// felhantering och extension-filtrering.
///
/// <para><b>Plugin-författaren implementerar:</b>
/// <list type="bullet">
///   <item><see cref="PluginId"/>, <see cref="DisplayName"/>, <see cref="Version"/>.</item>
///   <item><see cref="SupportedExtensions"/>.</item>
///   <item><see cref="Rules"/>.</item>
///   <item><see cref="AnalyzeFileAsync"/> — kärn-analysen per fil.</item>
/// </list>
/// </para>
/// </summary>
public abstract class AnalysisPluginBase : IAnalysisPlugin
{
    // ── Abstrakt manifest ─────────────────────────────────────────────────
    public abstract string                    PluginId           { get; }
    public abstract string                    DisplayName        { get; }
    public abstract string                    Version            { get; }
    public abstract IReadOnlyList<string>     SupportedExtensions { get; }
    public abstract IReadOnlyList<IPluginRule> Rules             { get; }

    protected readonly ILogger Logger;

    protected AnalysisPluginBase(ILogger logger)
    {
        Logger = logger;
    }

    // ── IAnalysisPlugin.AnalyzeAsync ──────────────────────────────────────

    /// <summary>
    /// Orkestrerar analysen: validerar input, mäter tid, fångar undantag
    /// och delegerar till <see cref="AnalyzeFileAsync"/>.
    /// </summary>
    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Kontrollera att pluginet stöder filändelsen
        var ext = Path.GetExtension(request.FilePath);
        if (!this.Supports(ext))
        {
            return new AnalysisResult
            {
                FilePath = request.FilePath,
                Language = DisplayName,
                Issues   = [],
                Duration = sw.Elapsed,
                Errors   = [$"Plugin '{PluginId}' stöder inte filändelse '{ext}'."]
            };
        }

        try
        {
            var issues = await AnalyzeFileAsync(request);

            // Filtrera bort deaktiverade regler
            var filtered = FilterByEnabledRules(issues, request.EnabledRuleIds);

            sw.Stop();
            Logger.LogDebug(
                "{Plugin} analyserade {File}: {Count} issues på {Ms}ms.",
                PluginId, Path.GetFileName(request.FilePath), filtered.Count, sw.ElapsedMilliseconds);

            return new AnalysisResult
            {
                FilePath = request.FilePath,
                Language = DisplayName,
                Issues   = filtered,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "{Plugin} misslyckades på {File}.", PluginId, request.FilePath);
            return new AnalysisResult
            {
                FilePath = request.FilePath,
                Language = DisplayName,
                Issues   = [],
                Duration = sw.Elapsed,
                Errors   = [$"Intern plugin-error i '{PluginId}': {ex.Message}"]
            };
        }
    }

    // ── Abstrakt kärna ────────────────────────────────────────────────────

    /// <summary>
    /// Plugin-specifik analys. Kasta aldrig uncaught exceptions — returnera
    /// felaktiga issues hellre (basen fångar och wrappar).
    /// </summary>
    protected abstract Task<IReadOnlyList<RawIssue>> AnalyzeFileAsync(AnalysisRequest request);

    // ── Hjälpmetoder för plugin-implementationer ──────────────────────────

    /// <summary>Bygger ett RawIssue med minimalt boilerplate.</summary>
    protected RawIssue MakeIssue(
        string      ruleId,
        LogicalScope scope,
        string      filePath,
        int         startLine,
        string      snippet,
        string      message,
        Severity severity,
        string      category,
        string?     suggestion      = null,
        bool        isAutoFixable   = false,
        string?     fixedSnippet    = null,
        int         endLine         = 0,
        int         startColumn     = 0,
        IReadOnlyDictionary<string, string>? metadata = null) => new()
    {
        RuleId       = ruleId,
        Scope        = scope,
        FilePath     = filePath,
        StartLine    = startLine,
        EndLine      = endLine > 0 ? endLine : startLine,
        StartColumn  = startColumn,
        Snippet      = snippet.Trim(),
        Message      = message,
        Suggestion   = suggestion,
        Severity     = severity,
        Category     = category,
        IsAutoFixable = isAutoFixable,
        FixedSnippet  = fixedSnippet,
        Metadata      = metadata ?? new Dictionary<string, string>()
    };

    /// <summary>
    /// Identifierar kommentarssyntax för aktuell fil.
    /// Plugin-implementationer kan använda detta för SnippetNormalizer.
    /// </summary>
    protected CommentStyle GetCommentStyle() =>
        SnippetNormalizer.DetectStyle(
            SupportedExtensions.FirstOrDefault() ?? ".cs");

    // ── Privata hjälpmetoder ──────────────────────────────────────────────

    private static IReadOnlyList<RawIssue> FilterByEnabledRules(
        IReadOnlyList<RawIssue> issues,
        IReadOnlySet<string>?   enabledRuleIds)
    {
        if (enabledRuleIds is null) return issues;
        return issues
            .Where(i => enabledRuleIds.Contains(i.RuleId))
            .ToList()
            .AsReadOnly();
    }

    public bool Supports(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return SupportedExtensions.Any(e =>
            string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
    }

}

// ═══════════════════════════════════════════════════════════════════════════
// PluginRuleBase — hjälpbasklass för regler
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bekvämlighets-record för att deklarera regelmetadata inline i ett plugin.
/// </summary>
public sealed record PluginRuleDescriptor : IPluginRule
{
    public required string  RuleId          { get; init; }
    public required string  Name            { get; init; }
    public required string  Description     { get; init; }
    public required string  Category        { get; init; }
    public Severity DefaultSeverity { get; init; } = Severity.Medium;
    public bool             IsEnabled        { get; init; } = true;
    public Uri?             DocumentationUri { get; init; }

}
