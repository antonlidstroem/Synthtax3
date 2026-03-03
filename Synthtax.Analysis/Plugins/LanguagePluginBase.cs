using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Plugins;

/// <summary>
/// Abstract base class for language plugins.
/// Handles directory traversal, parallelism and error isolation.
/// </summary>
public abstract class LanguagePluginBase : ILanguagePlugin
{
    protected readonly ILogger Logger;

    public abstract string Language                       { get; }
    public abstract string Version                        { get; }
    public abstract IReadOnlyList<string>       SupportedExtensions { get; }
    public abstract IReadOnlyList<ILanguageRule> Rules               { get; }

    protected LanguagePluginBase(ILogger logger) => Logger = logger;

    // ── AnalyzeFileAsync: run every enabled rule ──────────────────────────

    public virtual async Task<WebFileResultDto> AnalyzeFileAsync(
        string filePath, CancellationToken ct = default)
    {
        var result = new WebFileResultDto
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Language = Language
        };
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            foreach (var rule in Rules.Where(r => r.IsEnabled))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    result.Issues.AddRange(rule.Analyze(content, filePath, ct));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[{Lang}] Rule {Id} threw on {File}",
                        Language, rule.RuleId, filePath);
                }
            }
            result.IssueCount = result.Issues.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[{Lang}] AnalyzeFileAsync failed for {File}", Language, filePath);
            result.Errors.Add(ex.Message);
        }
        return result;
    }

    // ── AnalyzeDirectoryAsync: parallel walk ──────────────────────────────

    public virtual async Task<List<WebFileResultDto>> AnalyzeDirectoryAsync(
        string directoryPath, bool recursive = true, CancellationToken ct = default)
    {
        var opt   = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = SupportedExtensions
            .SelectMany(ext => Directory.GetFiles(directoryPath, $"*{ext}", opt))
            .Where(f => !IsExcluded(f))
            .ToList();

        var bag = new ConcurrentBag<WebFileResultDto>();
        await Parallel.ForEachAsync(files,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (file, token) => bag.Add(await AnalyzeFileAsync(file, token)));

        return [.. bag.OrderBy(r => r.FilePath)];
    }

    // ── Exclusion helper ──────────────────────────────────────────────────

    protected static bool IsExcluded(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/node_modules/")
            || p.Contains("/dist/")
            || p.Contains("/build/")
            || p.Contains("/obj/")
            || p.Contains("/.git/")
            || p.Contains("/coverage/")
            || p.Contains("/wwwroot/lib/")
            || p.Contains(".min.")
            || Path.GetFileName(p).StartsWith(".", StringComparison.Ordinal); // ← was char '.'
    }

    // ── Convenience factory ───────────────────────────────────────────────

    protected WebIssueDto MakeIssue(
        string ruleId, string issueType, string title, string description,
        string filePath, int line, Severity severity, string category,
        string? snippet = null, string? recommendation = null,
        bool autoFix = false, string? fixedCode = null, int endLine = 0) => new()
    {
        FilePath       = filePath,
        FileName       = Path.GetFileName(filePath),
        Language       = Language,
        RuleId         = ruleId,
        IssueType      = issueType,
        Title          = title,
        Description    = description,
        Recommendation = recommendation,
        LineNumber     = line,
        EndLine        = endLine > 0 ? endLine : line,
        CodeSnippet    = snippet,
        Severity       = severity,
        Category       = category,
        IsAutoFixable  = autoFix,
        FixedCode      = fixedCode
    };
}
