using System.Text;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Core.PromptFactory;

/// <summary>
/// Delade byggstenar för prompt-renderare.
/// Statiska hjälpmetoder som alla ITargetFormatter:s kan använda.
/// </summary>
internal static class PromptBuilder
{
    // ── Gemensamma snippets ───────────────────────────────────────────────

    /// <summary>Formaterar platsinformation som en läsbar rad.</summary>
    internal static string FormatLocation(RawIssue issue) =>
        $"{issue.FilePath}:{issue.StartLine}" +
        (issue.Scope.MemberName is not null ? $" [{issue.Scope.Kind}: {issue.Scope.MemberName}]" : "");

    /// <summary>Formaterar severity med emoji för läsbarhet.</summary>
    internal static string FormatSeverity(Severity severity) => severity switch
    {
        Severity.Critical => "🔴 Critical",
        Severity.High     => "🟠 High",
        Severity.Medium   => "🟡 Medium",
        Severity.Low      => "🟢 Low",
        _                 => severity.ToString()
    };

    /// <summary>
    /// Trimmar ett kodsnippet för display — avlägsnar gemensam indentering
    /// och begränsar till max <paramref name="maxLines"/> rader.
    /// </summary>
    internal static string TrimSnippet(string? snippet, int maxLines = 20)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return "(ingen kod tillgänglig)";

        var lines = snippet.Split('\n');
        if (lines.Length > maxLines)
            lines = lines.Take(maxLines).Append($"  … ({lines.Length - maxLines} rader till)").ToArray();

        // Ta bort gemensam indentering
        var minIndent = lines
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        return string.Join('\n', lines.Select(l =>
            l.Length >= minIndent ? l[minIndent..] : l));
    }

    /// <summary>Bygger ett kodblock med språkspecifik syntax-highlighting-hint.</summary>
    internal static string CodeBlock(string? code, string language = "csharp") =>
        $"```{language}\n{TrimSnippet(code)}\n```";

    /// <summary>
    /// Bygger en kompakt "breadcrumb" av scope-sökvägen.
    /// T.ex. "Acme.Payments → PaymentService → ProcessRefund"
    /// </summary>
    internal static string BuildScopePath(RawIssue issue)
    {
        var parts = new List<string>();
        if (issue.Scope.Namespace  is not null) parts.Add(issue.Scope.Namespace);
        if (issue.Scope.ClassName  is not null) parts.Add(issue.Scope.ClassName);
        if (issue.Scope.MemberName is not null) parts.Add(issue.Scope.MemberName);
        return parts.Count > 0 ? string.Join(" → ", parts) : issue.FilePath;
    }

    /// <summary>
    /// Extraherar ett numeriskt värde ur RawIssue.Metadata om det finns.
    /// Returnerar fallback-värdet om nyckeln saknas.
    /// </summary>
    internal static string GetMeta(RawIssue issue, string key, string fallback = "?") =>
        issue.Metadata.TryGetValue(key, out var v) ? v : fallback;
}
