namespace Synthtax.Core.Contracts;

// ═══════════════════════════════════════════════════════════════════════════
// IAnalysisPlugin  — det universella plugin-kontraktet
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Universellt plugin-interface för alla language-analysatorer.
///
/// <para><b>Designregel:</b> Core-projektet vet ingenting om implementeringen.
/// Roslyn, tree-sitter, regex, extern process — allt är tillåtet på plugin-sidan
/// så länge output är <see cref="RawIssue"/>.</para>
///
/// <para><b>Registrering:</b>
/// <code>
///   services.AddSingleton&lt;IAnalysisPlugin, CSharpAnalysisPlugin&gt;();
///   services.AddSingleton&lt;IAnalysisPlugin, PythonAnalysisPlugin&gt;();
/// </code>
/// <see cref="IPluginRegistry"/> hittar alla via DI-enumeration.
/// </para>
/// </summary>
public interface IAnalysisPlugin
{
    // ── Manifest ───────────────────────────────────────────────────────────

    /// <summary>Unikt plugin-ID, t.ex. "csharp-roslyn", "python-regex".</summary>
    string PluginId { get; }

    /// <summary>Visningsnamn, t.ex. "C# Roslyn Analyzer".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Semantisk version av pluginet (SemVer).
    /// Lagras i <c>Rule.Version</c> — ändring triggar re-seed av regelmetadata.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Filändelser som pluginet hanterar, t.ex. [".cs"], [".py", ".pyw"].
    /// Jämförelse sker case-insensitivt (OrdinalIgnoreCase).
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Alla regler som pluginet kan applicera.</summary>
    IReadOnlyList<IPluginRule> Rules { get; }

    // ── Analys ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Analyserar en enskild fil och returnerar hittade issues.
    ///
    /// <para>Pluginet ansvarar för att:
    /// <list type="bullet">
    ///   <item>Respektera <c>request.EnabledRuleIds</c> (null = kör alla).</item>
    ///   <item>Fånga egna interna undantag och rapportera dem via <see cref="AnalysisResult.Errors"/>.</item>
    ///   <item>Kasta <see cref="OperationCanceledException"/> om <c>request.CancellationToken</c> signaleras.</item>
    ///   <item>ALDRIG läsa från filsystemet — använd <c>request.FileContent</c>.</item>
    /// </list>
    /// </para>
    /// </summary>
    Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request);

    // ── Kapabilitet ────────────────────────────────────────────────────────

    /// <summary>True om pluginet kan hantera filer med angiven filändelse.</summary>
    bool Supports(string fileExtension) =>
        SupportedExtensions.Any(e =>
            string.Equals(e, fileExtension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Valfri health-check. Returnerar null om pluginet är operativt,
    /// annars en felbeskrivning (t.ex. "MSBuild not found").
    /// </summary>
    Task<string?> GetHealthAsync() => Task.FromResult<string?>(null);
}

// ═══════════════════════════════════════════════════════════════════════════
// IPluginRule  — en enskild regel inom ett plugin
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Metadata för en regel inom ett <see cref="IAnalysisPlugin"/>.
/// Används av <see cref="Synthtax.Infrastructure.Data.Seeders.RuleSeedService"/>
/// för att synkronisera regelmetadata med databasen.
/// </summary>
public interface IPluginRule
{
    string                    RuleId          { get; }
    string                    Name            { get; }
    string                    Description     { get; }
    string                    Category        { get; }
    Synthtax.Core.Enums.Severity DefaultSeverity { get; }
    bool                      IsEnabled       { get; }

    /// <summary>
    /// Dokumentations-URL till regelns förklaring (OWASP, docs, etc.).
    /// Null om ingen extern dokumentation finns.
    /// </summary>
    Uri? DocumentationUri => null;
}

// ═══════════════════════════════════════════════════════════════════════════
// IPluginRegistry
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Central katalog för alla registrerade plugins.
/// Implementeras av infrastrukturlagret via DI-enumeration.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>Alla registrerade plugins.</summary>
    IReadOnlyList<IAnalysisPlugin> GetAll();

    /// <summary>Returnerar plugin(s) som hanterar angiven filändelse.</summary>
    IReadOnlyList<IAnalysisPlugin> GetFor(string fileExtension);

    /// <summary>Returnerar specifikt plugin på ID. Null om det inte är registrerat.</summary>
    IAnalysisPlugin? GetById(string pluginId);

    /// <summary>Flat lista över alla regler från alla plugins.</summary>
    IReadOnlyList<(IAnalysisPlugin Plugin, IPluginRule Rule)> GetAllRules();
}

// ═══════════════════════════════════════════════════════════════════════════
// PluginRegistryExtensions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Bekvämlighetsmetoder på <see cref="IPluginRegistry"/>.</summary>
public static class PluginRegistryExtensions
{
    /// <summary>Kör alla lämpliga plugins för en fil och slår ihop resultaten.</summary>
    public static async Task<IReadOnlyList<RawIssue>> AnalyzeFileAsync(
        this IPluginRegistry registry,
        AnalysisRequest request,
        CancellationToken ct = default)
    {
        var ext     = Path.GetExtension(request.FilePath);
        var plugins = registry.GetFor(ext);
        if (plugins.Count == 0) return [];

        var allIssues = new List<RawIssue>();

        foreach (var plugin in plugins)
        {
            ct.ThrowIfCancellationRequested();
            var result = await plugin.AnalyzeAsync(request with
            {
                CancellationToken = ct
            });
            allIssues.AddRange(result.Issues);
        }

        return allIssues.AsReadOnly();
    }
}
