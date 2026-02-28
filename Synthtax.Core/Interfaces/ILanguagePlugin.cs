using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Interfaces;

// ─────────────────────────────────────────────────────────────────────────────
// ILanguageRule  –  a single analysis rule for text-based (non-Roslyn) files
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One analysis rule for a non-Roslyn language (CSS, JavaScript, HTML, Python, Java…).
/// Receives the raw file content and returns zero or more issues.
/// </summary>
public interface ILanguageRule
{
    string   RuleId           { get; }
    string   Name             { get; }
    string   Description      { get; }
    Severity DefaultSeverity  { get; }
    bool     IsEnabled        { get; }

    IEnumerable<WebIssueDto> Analyze(
        string fileContent,
        string filePath,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// ILanguagePlugin  –  handles one language family
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A plugin that analyses files belonging to one language.
/// Register via DI:  services.AddSingleton&lt;ILanguagePlugin, CssPlugin&gt;()
/// The registry discovers all registered plugins automatically.
///
/// To add a new language (e.g. Python), implement ILanguagePlugin,
/// register it, and the rest of the pipeline picks it up without any changes.
/// </summary>
public interface ILanguagePlugin
{
    /// <summary>Human-readable language name, e.g. "CSS".</summary>
    string Language { get; }

    /// <summary>Semantic version string, e.g. "1.0.0".</summary>
    string Version  { get; }

    /// <summary>
    /// File extensions handled (lower-case, with leading dot): ".css", ".js" etc.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>All rules this plugin provides.</summary>
    IReadOnlyList<ILanguageRule> Rules { get; }

    /// <summary>Analyse a single file and return its findings.</summary>
    Task<WebFileResultDto> AnalyzeFileAsync(
        string filePath,
        CancellationToken ct = default);

    /// <summary>
    /// Analyse a whole directory tree.
    /// Default impl walks the directory and calls AnalyzeFileAsync in parallel.
    /// Override for cross-file checks (e.g. CSS unused-selector detection needs HTML files).
    /// </summary>
    Task<List<WebFileResultDto>> AnalyzeDirectoryAsync(
        string directoryPath,
        bool recursive = true,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// ILanguagePluginRegistry  –  lookup table
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Discovers all ILanguagePlugin instances registered in DI.</summary>
public interface ILanguagePluginRegistry
{
    IReadOnlyList<ILanguagePlugin> AllPlugins      { get; }
    ILanguagePlugin?               GetByExtension(string extension);
    ILanguagePlugin?               GetByLanguage(string language);
}

// ─────────────────────────────────────────────────────────────────────────────
// IWebLanguageAnalysisService  –  top-level service
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs all applicable language plugins over a project directory or a single file.
/// </summary>
public interface IWebLanguageAnalysisService
{
    Task<WebAnalysisResultDto> AnalyzeDirectoryAsync(
        string directoryPath,
        bool recursive = true,
        CancellationToken ct = default);

    Task<WebFileResultDto> AnalyzeFileAsync(
        string filePath,
        CancellationToken ct = default);

    List<LanguagePluginInfoDto> GetRegisteredPlugins();
}

// ─────────────────────────────────────────────────────────────────────────────
// ICommitMessageService  –  rule-based commit message suggestion
// ─────────────────────────────────────────────────────────────────────────────

public interface ICommitMessageService
{
    /// <summary>
    /// Compares uncommitted changes (working tree or staged) against HEAD and
    /// returns a rule-based Conventional Commits suggestion.  No AI involved.
    /// </summary>
    Task<CommitSuggestionDto> SuggestAsync(
        string repositoryPath,
        bool   stagedOnly = false,
        CancellationToken ct = default);
}
