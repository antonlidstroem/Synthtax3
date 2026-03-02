using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;
using Synthtax.Domain.Enums;

namespace Synthtax.Analysis.Languages.Css;

// ══════════════════════════════════════════════════════════════════════════════
// CSS Plugin
// ══════════════════════════════════════════════════════════════════════════════

public class CssPlugin : Plugins.LanguagePluginBase
{
    public override string Language { get; } = "CSS";
    public override string Version  { get; } = "1.0.0";
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".css", ".scss", ".less"];

    public override IReadOnlyList<ILanguageRule> Rules { get; }

    public CssPlugin(ILogger<CssPlugin> logger) : base(logger)
    {
        Rules = new List<ILanguageRule>
        {
            new DuplicatePropertyRule(),
            new DuplicateSelectorRule(),
            new ImportantOveruseRule(),
            new HighSpecificityRule(),
            new MissingVendorPrefixRule(),
            new EmptyRuleRule(),
            new MagicNumberRule(),
            new ColorFormatInconsistencyRule(),
            new LargeFileRule(),
            new DeadMediaQueryRule()
        }.AsReadOnly();
    }

    // Override to support cross-file unused-selector detection:
    // scan CSS files for selectors, then check HTML/JS files to see which are referenced.
    public override async Task<List<WebFileResultDto>> AnalyzeDirectoryAsync(
        string directoryPath, bool recursive = true, CancellationToken ct = default)
    {
        var baseResults = await base.AnalyzeDirectoryAsync(directoryPath, recursive, ct);

        // Gather all CSS selectors across the project
        var allSelectors = await CollectSelectorsAsync(baseResults, ct);

        // Gather HTML/JS class and id references
        var usedIdentifiers = await CollectHtmlUsageAsync(directoryPath, recursive, ct);

        // Add unused-selector issues
        var unusedRule = new UnusedSelectorRule(usedIdentifiers);
        foreach (var fileResult in baseResults)
        {
            try
            {
                var content = await File.ReadAllTextAsync(fileResult.FilePath, ct);
                fileResult.Issues.AddRange(unusedRule.Analyze(content, fileResult.FilePath, ct));
                fileResult.IssueCount = fileResult.Issues.Count;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Logger.LogWarning(ex, "UnusedSelectorRule failed on {F}", fileResult.FilePath); }
        }

        return baseResults;
    }

    private static async Task<HashSet<string>> CollectSelectorsAsync(
        List<WebFileResultDto> _, CancellationToken ct)
    {
        // Placeholder – selectors are re-parsed inside UnusedSelectorRule per file
        await Task.CompletedTask;
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<string>> CollectHtmlUsageAsync(
        string dir, bool recursive, CancellationToken ct)
    {
        var used  = new ConcurrentBag<string>();
        var opt   = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(dir, "*.*", opt)
            .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".htm",  StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jsx",  StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".tsx",  StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".js",   StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".ts",   StringComparison.OrdinalIgnoreCase))
            .Where(f => !Plugins.LanguagePluginBase.IsExcluded(f))
            .ToList();

        await Parallel.ForEachAsync(files,
            new ParallelOptions { CancellationToken = ct },
            async (file, token) =>
            {
                var text = await File.ReadAllTextAsync(file, token);
                // class="foo bar"
                foreach (Match m in Regex.Matches(text, @"class\s*=\s*[""']([^""']+)[""']"))
                    foreach (var cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        used.Add("." + cls.Trim());
                // id="foo"
                foreach (Match m in Regex.Matches(text, @"\bid\s*=\s*[""']([^""']+)[""']"))
                    used.Add("#" + m.Groups[1].Value.Trim());
                // className="foo" (React)
                foreach (Match m in Regex.Matches(text, @"className\s*=\s*[""']([^""']+)[""']"))
                    foreach (var cls in m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        used.Add("." + cls.Trim());
            });

        return used.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Rules
// ══════════════════════════════════════════════════════════════════════════════

// WEB001 – Duplicate property within same rule block
file sealed class DuplicatePropertyRule : ILanguageRule
{
    public string   RuleId          => "WEB001";
    public string   Name            => "Duplicate property";
    public string   Description     => "Property declared more than once in the same rule block; only the last wins.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex BlockRx = new(
        @"([^{]+)\{([^}]*)\}", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PropRx  = new(
        @"^\s*([\w-]+)\s*:", RegexOptions.Multiline | RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        foreach (Match block in BlockRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var blockBody  = block.Groups[2].Value;
            var props      = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var blockStart = content[..block.Index].Count(c => c == '\n') + 1;

            foreach (Match prop in PropRx.Matches(blockBody))
            {
                var name = prop.Groups[1].Value.ToLowerInvariant();
                var lineInBlock = blockBody[..prop.Index].Count(c => c == '\n');
                var absLine = blockStart + lineInBlock;

                if (props.TryGetValue(name, out var prevLine))
                    yield return Issue(filePath, absLine, name, prevLine);
                else
                    props[name] = absLine;
            }
        }
    }

    private static WebIssueDto Issue(string fp, int line, string prop, int firstLine) => new()
    {
        FilePath = fp, FileName = Path.GetFileName(fp), Language = "CSS",
        RuleId = "WEB001", IssueType = "DuplicateProperty",
        Title = $"Duplicate property '{prop}'",
        Description = $"'{prop}' is declared again (first at line {firstLine}). Only the last declaration takes effect.",
        Recommendation = "Remove the earlier declaration or merge them intentionally.",
        LineNumber = line, Severity = Severity.Medium, Category = "Maintainability"
    };
}

// WEB002 – Duplicate selector across the file
file sealed class DuplicateSelectorRule : ILanguageRule
{
    public string   RuleId          => "WEB002";
    public string   Name            => "Duplicate selector";
    public string   Description     => "The same selector appears in multiple rule blocks. Rules should be merged.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex SelectorRx = new(
        @"^([^{/@][^{]*?)\s*\{", RegexOptions.Multiline | RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in SelectorRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var selector = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(selector) || selector.Contains("@")) continue;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            if (seen.TryGetValue(selector, out var prev))
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                    RuleId = "WEB002", IssueType = "DuplicateSelector",
                    Title = $"Duplicate selector '{selector}'",
                    Description = $"Selector first defined at line {prev}; redefined here.",
                    Recommendation = "Merge the two rule blocks into one.",
                    LineNumber = line, Severity = Severity.Low, Category = "Maintainability",
                    CodeSnippet = selector
                };
            else
                seen[selector] = line;
        }
    }
}

// WEB003 – !important overuse
file sealed class ImportantOveruseRule : ILanguageRule
{
    public string   RuleId          => "WEB003";
    public string   Name            => "!important overuse";
    public string   Description     => "Excessive use of !important makes styles hard to override and debug.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex ImportantRx = new(@"!important", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var hits = ImportantRx.Matches(content);
        if (hits.Count < 3) yield break;  // 1-2 is forgivable

        foreach (Match m in hits)
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            var lineText = content.Split('\n')[line - 1].Trim();
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                RuleId = "WEB003", IssueType = "ImportantOveruse",
                Title = "!important used",
                Description = $"File contains {hits.Count} !important declarations. This makes the cascade unpredictable.",
                Recommendation = "Refactor specificity instead of using !important.",
                LineNumber = line, Severity = Severity.Medium, Category = "Maintainability",
                CodeSnippet = lineText
            };
        }
    }
}

// WEB004 – High specificity selector (id-chains, deeply nested)
file sealed class HighSpecificityRule : ILanguageRule
{
    public string   RuleId          => "WEB004";
    public string   Name            => "High specificity";
    public string   Description     => "Selectors with very high specificity are hard to override.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex SelectorRx = new(
        @"^([^{/@][^{]*?)\s*\{", RegexOptions.Multiline | RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in SelectorRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var selector = m.Groups[1].Value.Trim();
            var (a, b, c) = CalcSpecificity(selector);
            if (a > 1 || b > 3 || (a == 0 && b == 0 && c > 5))
            {
                var line = content[..m.Index].Count(c2 => c2 == '\n') + 1;
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                    RuleId = "WEB004", IssueType = "HighSpecificity",
                    Title = $"High specificity ({a},{b},{c})",
                    Description = $"Selector '{selector}' has specificity ({a},{b},{c}). High specificity leads to cascade wars.",
                    Recommendation = "Use BEM/OOCSS methodology to keep specificity flat.",
                    LineNumber = line, Severity = Severity.Low, Category = "Maintainability",
                    CodeSnippet = selector
                };
            }
        }
    }

    private static (int a, int b, int c) CalcSpecificity(string selector)
    {
        int a = Regex.Matches(selector, @"#[\w-]+").Count;
        int b = Regex.Matches(selector, @"\.[\w-]+|\[.+?\]|:(?!:)[\w-]+").Count;
        int c = Regex.Matches(selector, @"(?<![.#])(?:^|\s|[>+~])[\w-]+").Count;
        return (a, b, c);
    }
}

// WEB005 – Missing vendor prefix
file sealed class MissingVendorPrefixRule : ILanguageRule
{
    public string   RuleId          => "WEB005";
    public string   Name            => "Missing vendor prefix";
    public string   Description     => "Some properties need vendor prefixes for broader browser support.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly (string property, string[] prefixedVersions)[] NeedPrefix =
    [
        ("transform",          ["-webkit-transform"]),
        ("animation",          ["-webkit-animation"]),
        ("transition",         ["-webkit-transition"]),
        ("appearance",         ["-webkit-appearance", "-moz-appearance"]),
        ("user-select",        ["-webkit-user-select", "-moz-user-select", "-ms-user-select"]),
        ("backface-visibility",["-webkit-backface-visibility"]),
    ];

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[i].Trim();
            foreach (var (prop, prefixed) in NeedPrefix)
            {
                if (!Regex.IsMatch(line, $@"^\s*{Regex.Escape(prop)}\s*:", RegexOptions.IgnoreCase)) continue;
                var missing = prefixed.Where(p => !content.Contains(p, StringComparison.OrdinalIgnoreCase)).ToList();
                if (missing.Count == 0) continue;
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                    RuleId = "WEB005", IssueType = "MissingVendorPrefix",
                    Title = $"Missing vendor prefix for '{prop}'",
                    Description = $"'{prop}' may need: {string.Join(", ", missing)}",
                    Recommendation = $"Add {string.Join(", ", missing)} before the standard property.",
                    LineNumber = i + 1, Severity = Severity.Low, Category = "Compatibility",
                    CodeSnippet = lines[i].Trim()
                };
            }
        }
    }
}

// WEB006 – Empty rule block
file sealed class EmptyRuleRule : Core.Interfaces.ILanguageRule
{
    public string   RuleId          => "WEB006";
    public string   Name            => "Empty rule block";
    public string   Description     => "Rule block contains no declarations. It is dead code.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex EmptyRx = new(
        @"([^{/]+)\{\s*\}", RegexOptions.Compiled | RegexOptions.Singleline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in EmptyRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var selector = m.Groups[1].Value.Trim();
            if (selector.StartsWith("//") || selector.StartsWith("/*")) continue;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                RuleId = "WEB006", IssueType = "EmptyRule",
                Title = $"Empty rule '{selector}'",
                Description = "This selector has no declarations and can be removed.",
                Recommendation = "Delete the empty rule block.",
                LineNumber = line, Severity = Severity.Low, Category = "Maintainability",
                CodeSnippet = m.Value.Trim(), IsAutoFixable = true,
                FixedCode = string.Empty
            };
        }
    }
}

// WEB007 – Magic number in px values
file sealed class MagicNumberRule : ILanguageRule
{
    public string   RuleId          => "WEB007";
    public string   Name            => "Magic number";
    public string   Description     => "Arbitrary pixel values that are not multiples of a design-system unit.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex PxRx = new(@"\b(\d+)px\b", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            foreach (Match m in PxRx.Matches(lines[i]))
            {
                if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
                // Common base-8 / base-4 design systems; 0 is fine
                if (n == 0 || n % 4 == 0 || n == 1) continue;
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                    RuleId = "WEB007", IssueType = "MagicNumber",
                    Title = $"Magic number: {n}px",
                    Description = $"{n}px is not a multiple of 4. Consider using a design-system spacing token.",
                    Recommendation = "Use CSS variables or multiples of your base unit (4px or 8px).",
                    LineNumber = i + 1, Severity = Severity.Low, Category = "Consistency",
                    CodeSnippet = lines[i].Trim()
                };
            }
        }
    }
}

// WEB008 – Inconsistent color format
file sealed class ColorFormatInconsistencyRule : ILanguageRule
{
    public string   RuleId          => "WEB008";
    public string   Name            => "Inconsistent color format";
    public string   Description     => "File mixes hex, rgb(), rgba(), hsl() and named colors.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var hasHex     = Regex.IsMatch(content, @"#[0-9a-fA-F]{3,8}\b");
        var hasRgb     = Regex.IsMatch(content, @"\brgba?\s*\(");
        var hasHsl     = Regex.IsMatch(content, @"\bhsla?\s*\(");
        var formatCount = new[] { hasHex, hasRgb, hasHsl }.Count(b => b);

        if (formatCount < 2) yield break;

        yield return new WebIssueDto
        {
            FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
            RuleId = "WEB008", IssueType = "ColorFormatInconsistency",
            Title = "Inconsistent color format",
            Description = $"File uses {(hasHex?"hex ":"")}{(hasRgb?"rgb() ":"")}{(hasHsl?"hsl() ":"")}formats. Pick one.",
            Recommendation = "Standardise on hex or custom properties for colors.",
            LineNumber = 1, Severity = Severity.Low, Category = "Consistency"
        };
    }
}

// WEB009 – Large CSS file
file sealed class LargeFileRule : ILanguageRule
{
    private const int LineThreshold = 500;
    public string   RuleId          => "WEB009";
    public string   Name            => "Large CSS file";
    public string   Description     => $"File exceeds {LineThreshold} lines. Consider splitting into modules.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n').Length;
        if (lines <= LineThreshold) yield break;
        yield return new WebIssueDto
        {
            FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
            RuleId = "WEB009", IssueType = "LargeFile",
            Title = $"Large CSS file ({lines} lines)",
            Description = $"Files over {LineThreshold} lines are hard to maintain.",
            Recommendation = "Split into feature-specific partials (SCSS @use / CSS @import).",
            LineNumber = 1, Severity = Severity.Medium, Category = "Maintainability"
        };
    }
}

// WEB010 – Dead media query (impossible conditions like min-width > max-width)
file sealed class DeadMediaQueryRule : ILanguageRule
{
    public string   RuleId          => "WEB010";
    public string   Name            => "Dead media query";
    public string   Description     => "Media query with logically impossible conditions, e.g. min-width > max-width.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex MediaRx = new(
        @"@media[^{]*min-width\s*:\s*(\d+)px[^{]*max-width\s*:\s*(\d+)px",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in MediaRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            if (!int.TryParse(m.Groups[1].Value, out var minW) ||
                !int.TryParse(m.Groups[2].Value, out var maxW)) continue;
            if (minW <= maxW) continue;
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                RuleId = "WEB010", IssueType = "DeadMediaQuery",
                Title = $"Dead media query (min-width {minW}px > max-width {maxW}px)",
                Description = "The min-width is larger than max-width; this query never matches.",
                Recommendation = "Fix the pixel values or remove the block.",
                LineNumber = line, Severity = Severity.Medium, Category = "Dead code",
                CodeSnippet = m.Value.Trim()
            };
        }
    }
}

// WEB011 – Unused CSS selectors (requires HTML context, injected from CssPlugin.AnalyzeDirectoryAsync)
file sealed class UnusedSelectorRule : ILanguageRule
{
    private readonly HashSet<string> _usedIdentifiers;

    public UnusedSelectorRule(HashSet<string> usedIdentifiers) => _usedIdentifiers = usedIdentifiers;

    public string   RuleId          => "WEB011";
    public string   Name            => "Unused selector";
    public string   Description     => "CSS selector not referenced in any HTML/JS/TS file in the project.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex SelectorRx = new(
        @"^([^{/@\s][^{]*?)\s*\{", RegexOptions.Multiline | RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        if (_usedIdentifiers.Count == 0) yield break;   // No HTML scanned – skip

        foreach (Match m in SelectorRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var selector = m.Groups[1].Value.Trim();
            if (string.IsNullOrWhiteSpace(selector) || selector.Contains('@')) continue;

            // Extract the simple class or id tokens from compound selectors
            var classTokens = Regex.Matches(selector, @"\.([\w-]+)").Cast<Match>().Select(x => "." + x.Groups[1].Value);
            var idTokens    = Regex.Matches(selector, @"#([\w-]+)").Cast<Match>().Select(x => "#" + x.Groups[1].Value);
            var tokens      = classTokens.Concat(idTokens).ToList();

            if (tokens.Count == 0) continue;  // element selector – skip
            if (tokens.Any(t => _usedIdentifiers.Contains(t))) continue;  // used

            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "CSS",
                RuleId = "WEB011", IssueType = "UnusedSelector",
                Title = $"Possibly unused selector '{selector}'",
                Description = "No HTML/JS file in the project references this selector.",
                Recommendation = "Remove the rule if no longer needed.",
                LineNumber = line, Severity = Severity.Medium, Category = "Dead code",
                CodeSnippet = selector
            };
        }
    }
}
