using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Languages.Html;

// ══════════════════════════════════════════════════════════════════════════════
// HTML Plugin
// ══════════════════════════════════════════════════════════════════════════════

public class HtmlPlugin : Plugins.LanguagePluginBase
{
    public override string Language { get; } = "HTML";
    public override string Version  { get; } = "1.0.0";
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".html", ".htm"];

    public override IReadOnlyList<ILanguageRule> Rules { get; }

    public HtmlPlugin(ILogger<HtmlPlugin> logger) : base(logger)
    {
        Rules = new List<ILanguageRule>
        {
            new MissingAltRule(),
            new InlineStyleRule(),
            new DeprecatedTagRule(),
            new MissingLangRule(),
            new DuplicateIdRule(),
            new InlineScriptRule(),
            new FormMissingAttributesRule(),
            new MissingViewportRule(),
            new LargeHtmlFileRule(),
            new EmptyHeadingRule()
        }.AsReadOnly();
    }
}

// ─── Rules ───────────────────────────────────────────────────────────────────

// WEB201 – <img> without alt attribute
file sealed class MissingAltRule : ILanguageRule
{
    public string   RuleId          => "WEB201";
    public string   Name            => "Image missing alt attribute";
    public string   Description     => "<img> without alt breaks accessibility (WCAG 2.1 AA).";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex ImgRx = new(
        @"<img\s[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in ImgRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            if (m.Value.Contains("alt=", StringComparison.OrdinalIgnoreCase)) continue;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            var src  = Regex.Match(m.Value, @"src\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase).Groups[1].Value;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                RuleId = "WEB201", IssueType = "MissingAlt",
                Title = "Image missing alt attribute",
                Description = $"<img src=\"{src}\"> has no alt attribute. Screen readers cannot describe it.",
                Recommendation = "Add alt=\"descriptive text\" or alt=\"\" for decorative images.",
                LineNumber = line, Severity = Severity.High, Category = "Accessibility",
                CodeSnippet = m.Value.Length > 100 ? m.Value[..100] + "…" : m.Value
            };
        }
    }
}

// WEB202 – Inline style attributes
file sealed class InlineStyleRule : ILanguageRule
{
    public string   RuleId          => "WEB202";
    public string   Name            => "Inline style";
    public string   Description     => "Inline style attributes mix content and presentation, making styles hard to maintain.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"<[a-zA-Z][^>]*\bstyle\s*=\s*[""'][^""']+[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in Rx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                RuleId = "WEB202", IssueType = "InlineStyle",
                Title = "Inline style attribute",
                Description = "style=\"…\" is hard to override and scatters CSS across HTML.",
                Recommendation = "Move styles to a CSS class.",
                LineNumber = line, Severity = Severity.Low, Category = "Maintainability",
                CodeSnippet = (m.Value.Length > 100 ? m.Value[..100] + "…" : m.Value)
            };
        }
    }
}

// WEB203 – Deprecated HTML tags
file sealed class DeprecatedTagRule : ILanguageRule
{
    public string   RuleId          => "WEB203";
    public string   Name            => "Deprecated HTML tag";
    public string   Description     => "HTML tags removed in HTML5 should not be used.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly string[] DeprecatedTags =
    [
        "acronym", "applet", "basefont", "big", "blink", "center",
        "dir", "font", "frame", "frameset", "isindex", "listing",
        "marquee", "menu", "nobr", "noframes", "plaintext", "s",
        "spacer", "strike", "tt", "xmp"
    ];

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (var tag in DeprecatedTags)
        {
            ct.ThrowIfCancellationRequested();
            var rx = new Regex($@"<{tag}[\s>]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            foreach (Match m in rx.Matches(content))
            {
                var line = content[..m.Index].Count(c => c == '\n') + 1;
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                    RuleId = "WEB203", IssueType = "DeprecatedTag",
                    Title = $"Deprecated tag <{tag}>",
                    Description = $"<{tag}> was removed in HTML5.",
                    Recommendation = $"Replace <{tag}> with a semantic alternative and CSS.",
                    LineNumber = line, Severity = Severity.Medium, Category = "Compatibility",
                    CodeSnippet = m.Value
                };
            }
        }
    }
}

// WEB204 – Missing lang on <html>
file sealed class MissingLangRule : ILanguageRule
{
    public string   RuleId          => "WEB204";
    public string   Name            => "Missing lang attribute on <html>";
    public string   Description     => "<html> without lang fails WCAG 3.1.1.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex HtmlTagRx = new(
        @"<html[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var m = HtmlTagRx.Match(content);
        if (!m.Success) yield break;
        if (m.Value.Contains("lang=", StringComparison.OrdinalIgnoreCase)) yield break;

        var line = content[..m.Index].Count(c => c == '\n') + 1;
        yield return new WebIssueDto
        {
            FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
            RuleId = "WEB204", IssueType = "MissingLang",
            Title = "Missing lang attribute on <html>",
            Description = "Screen readers and search engines need the language declaration.",
            Recommendation = "Add lang=\"sv\" (or the appropriate BCP 47 code).",
            LineNumber = line, Severity = Severity.High, Category = "Accessibility",
            CodeSnippet = m.Value,
            IsAutoFixable = true, FixedCode = m.Value.Replace("<html", "<html lang=\"en\"")
        };
    }
}

// WEB205 – Duplicate id attributes
file sealed class DuplicateIdRule : ILanguageRule
{
    public string   RuleId          => "WEB205";
    public string   Name            => "Duplicate id attribute";
    public string   Description     => "Multiple elements sharing the same id violate the HTML spec.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex IdRx = new(
        @"\bid\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in IdRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var id   = m.Groups[1].Value;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            if (seen.TryGetValue(id, out var prev))
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                    RuleId = "WEB205", IssueType = "DuplicateId",
                    Title = $"Duplicate id '{id}'",
                    Description = $"id=\"{id}\" already used at line {prev}. IDs must be unique per page.",
                    Recommendation = "Rename one of the elements; use class for shared styling.",
                    LineNumber = line, Severity = Severity.High, Category = "Correctness",
                    CodeSnippet = m.Value
                };
            else
                seen[id] = line;
        }
    }
}

// WEB206 – Inline <script> blocks
file sealed class InlineScriptRule : ILanguageRule
{
    public string   RuleId          => "WEB206";
    public string   Name            => "Inline script block";
    public string   Description     => "Script logic embedded directly in HTML is hard to test and maintain.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex ScriptRx = new(
        @"<script(?!\s+src=)[^>]*>([\s\S]*?)<\/script>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in ScriptRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var inner = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(inner)) continue;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                RuleId = "WEB206", IssueType = "InlineScript",
                Title = "Inline <script> block",
                Description = "Script code embedded in HTML. Move to an external .js file.",
                Recommendation = "Extract to a .js file and reference with <script src=\"…\">.",
                LineNumber = line, Severity = Severity.Medium, Category = "Maintainability",
                CodeSnippet = inner.Length > 120 ? inner[..120] + "…" : inner
            };
        }
    }
}

// WEB207 – <form> missing action or method
file sealed class FormMissingAttributesRule : ILanguageRule
{
    public string   RuleId          => "WEB207";
    public string   Name            => "Form missing action/method";
    public string   Description     => "<form> without explicit action or method relies on browser defaults.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex FormRx = new(
        @"<form\s[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in FormRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var tag  = m.Value;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            bool missingAction = !tag.Contains("action=", StringComparison.OrdinalIgnoreCase);
            bool missingMethod = !tag.Contains("method=", StringComparison.OrdinalIgnoreCase);
            if (!missingAction && !missingMethod) continue;
            var missing = new List<string>();
            if (missingAction) missing.Add("action");
            if (missingMethod) missing.Add("method");
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                RuleId = "WEB207", IssueType = "FormMissingAttributes",
                Title = $"Form missing {string.Join(" and ", missing)}",
                Description = $"<form> is missing {string.Join(", ", missing)} attribute(s).",
                Recommendation = $"Add explicit {string.Join(" and ", missing)} to clarify intent.",
                LineNumber = line, Severity = Severity.Medium, Category = "Best practice",
                CodeSnippet = tag
            };
        }
    }
}

// WEB208 – Missing viewport meta tag
file sealed class MissingViewportRule : ILanguageRule
{
    public string   RuleId          => "WEB208";
    public string   Name            => "Missing viewport meta tag";
    public string   Description     => "Without <meta name=\"viewport\"> the page is not mobile-friendly.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        // Only applies to full HTML documents (has <head>)
        if (!content.Contains("<head", StringComparison.OrdinalIgnoreCase)) yield break;
        if (Regex.IsMatch(content, @"<meta\s[^>]*name\s*=\s*[""']viewport[""']", RegexOptions.IgnoreCase)) yield break;
        yield return new WebIssueDto
        {
            FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
            RuleId = "WEB208", IssueType = "MissingViewport",
            Title = "Missing viewport meta tag",
            Description = "No <meta name=\"viewport\"> found. Page will not scale on mobile.",
            Recommendation = "Add <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"> inside <head>.",
            LineNumber = 1, Severity = Severity.High, Category = "Responsive design",
            IsAutoFixable = false
        };
    }
}

// WEB209 – Large HTML file
file sealed class LargeHtmlFileRule : ILanguageRule
{
    private const int LineThreshold = 300;
    public string   RuleId          => "WEB209";
    public string   Name            => "Large HTML file";
    public string   Description     => $"File exceeds {LineThreshold} lines. Consider splitting into components/partials.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n').Length;
        if (lines <= LineThreshold) yield break;
        yield return new WebIssueDto
        {
            FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
            RuleId = "WEB209", IssueType = "LargeFile",
            Title = $"Large HTML file ({lines} lines)",
            Description = $"Files over {LineThreshold} lines are hard to maintain.",
            Recommendation = "Extract sections into partial templates or web components.",
            LineNumber = 1, Severity = Severity.Low, Category = "Maintainability"
        };
    }
}

// WEB210 – Empty heading tags
file sealed class EmptyHeadingRule : ILanguageRule
{
    public string   RuleId          => "WEB210";
    public string   Name            => "Empty heading";
    public string   Description     => "Empty <h1>–<h6> tags confuse screen readers and hurt SEO.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"<(h[1-6])[^>]*>\s*<\/\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in Rx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "HTML",
                RuleId = "WEB210", IssueType = "EmptyHeading",
                Title = $"Empty <{m.Groups[1].Value}>",
                Description = $"<{m.Groups[1].Value}> has no text content.",
                Recommendation = "Add heading text or remove the element.",
                LineNumber = line, Severity = Severity.Medium, Category = "Accessibility",
                CodeSnippet = m.Value
            };
        }
    }
}
