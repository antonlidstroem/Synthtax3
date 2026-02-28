using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Languages.JavaScript;

// ══════════════════════════════════════════════════════════════════════════════
// JavaScript Plugin
// ══════════════════════════════════════════════════════════════════════════════

public class JavaScriptPlugin : Plugins.LanguagePluginBase
{
    public override string Language { get; } = "JavaScript";
    public override string Version  { get; } = "1.0.0";
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx"];

    public override IReadOnlyList<ILanguageRule> Rules { get; }

    public JavaScriptPlugin(ILogger<JavaScriptPlugin> logger) : base(logger)
    {
        Rules = new List<ILanguageRule>
        {
            new ConsoleLogRule(),
            new EvalUsageRule(),
            new VarUsageRule(),
            new LooseEqualityRule(),
            new DebuggerRule(),
            new TodoCommentRule(),
            new LongFunctionRule(),
            new DeepCallbackNestingRule(),
            new NoStrictModeRule(),
            new HardcodedUrlRule()
        }.AsReadOnly();
    }
}

// ─── Rules ───────────────────────────────────────────────────────────────────

// WEB101 – console.log left in production code
file sealed class ConsoleLogRule : ILanguageRule
{
    public string   RuleId          => "WEB101";
    public string   Name            => "console.log in production code";
    public string   Description     => "console.log (and console.error/warn) statements should not be committed.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"\bconsole\.(log|warn|error|info|debug|trace)\s*\(", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsLineComment(lines[i])) continue;
            foreach (Match m in Rx.Matches(lines[i]))
                yield return Issue(filePath, i + 1, $"console.{m.Groups[1].Value}()", lines[i].Trim());
        }
    }

    private static WebIssueDto Issue(string fp, int line, string call, string snippet) => new()
    {
        FilePath = fp, FileName = Path.GetFileName(fp), Language = "JavaScript",
        RuleId = "WEB101", IssueType = "ConsoleLog",
        Title = $"{call} in production code",
        Description = "Debug logging left in source. Consider removing or replacing with a proper logger.",
        Recommendation = "Remove console statements before committing or use a logging library.",
        LineNumber = line, Severity = Severity.Low, Category = "Code quality",
        CodeSnippet = snippet
    };

    private static bool IsLineComment(string line) => line.TrimStart().StartsWith("//");
}

// WEB102 – eval() usage
file sealed class EvalUsageRule : ILanguageRule
{
    public string   RuleId          => "WEB102";
    public string   Name            => "eval() usage";
    public string   Description     => "eval() executes arbitrary code and is a security and performance risk.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"\beval\s*\(", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsLineComment(lines[i])) continue;
            if (Rx.IsMatch(lines[i]))
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                    RuleId = "WEB102", IssueType = "EvalUsage",
                    Title = "eval() used",
                    Description = "eval() is a security vulnerability (XSS) and prevents JS engine optimisations.",
                    Recommendation = "Replace with JSON.parse(), Function constructor, or restructure the logic.",
                    LineNumber = i + 1, Severity = Severity.High, Category = "Security",
                    CodeSnippet = lines[i].Trim()
                };
        }
    }

    private static bool IsLineComment(string line) => line.TrimStart().StartsWith("//");
}

// WEB103 – var instead of let/const
file sealed class VarUsageRule : ILanguageRule
{
    public string   RuleId          => "WEB103";
    public string   Name            => "var declaration";
    public string   Description     => "var has function scope and is hoisted; prefer let or const.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"\bvar\s+", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (lines[i].TrimStart().StartsWith("//")) continue;
            if (Rx.IsMatch(lines[i]))
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                    RuleId = "WEB103", IssueType = "VarUsage",
                    Title = "var declaration",
                    Description = "var is function-scoped and hoisted. Use const or let for block scope.",
                    Recommendation = "Replace with const (if not reassigned) or let.",
                    LineNumber = i + 1, Severity = Severity.Low, Category = "Modern JS",
                    CodeSnippet = lines[i].Trim()
                };
        }
    }
}

// WEB104 – == instead of ===
file sealed class LooseEqualityRule : ILanguageRule
{
    public string   RuleId          => "WEB104";
    public string   Name            => "Loose equality (== / !=)";
    public string   Description     => "== and != perform type coercion which can cause subtle bugs.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"(?<![=!<>])={2}(?!=)|(?<![=!<>])!={1}(?!=)", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (lines[i].TrimStart().StartsWith("//")) continue;
            foreach (Match m in Rx.Matches(lines[i]))
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                    RuleId = "WEB104", IssueType = "LooseEquality",
                    Title = $"Loose equality '{m.Value}'",
                    Description = $"'{m.Value}' uses type coercion. '0 == false' is true.",
                    Recommendation = $"Replace with '{m.Value}=' for strict comparison.",
                    LineNumber = i + 1, Severity = Severity.Medium, Category = "Correctness",
                    CodeSnippet = lines[i].Trim(),
                    IsAutoFixable = true,
                    FixedCode = lines[i].Replace(m.Value, m.Value + "=")
                };
        }
    }
}

// WEB105 – debugger statement
file sealed class DebuggerRule : ILanguageRule
{
    public string   RuleId          => "WEB105";
    public string   Name            => "debugger statement";
    public string   Description     => "debugger statements pause execution in DevTools and must not be committed.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"^\s*debugger\s*;?\s*$", RegexOptions.Compiled | RegexOptions.Multiline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in Rx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                RuleId = "WEB105", IssueType = "Debugger",
                Title = "debugger statement",
                Description = "debugger pauses execution in the browser. Remove before committing.",
                Recommendation = "Delete the debugger statement.",
                LineNumber = line, Severity = Severity.High, Category = "Code quality",
                IsAutoFixable = true, FixedCode = string.Empty
            };
        }
    }
}

// WEB106 – TODO/FIXME/HACK comments
file sealed class TodoCommentRule : ILanguageRule
{
    public string   RuleId          => "WEB106";
    public string   Name            => "TODO/FIXME comment";
    public string   Description     => "Outstanding TODO, FIXME, or HACK comments indicate unfinished work.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"//.*\b(TODO|FIXME|HACK|XXX)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = Rx.Match(lines[i]);
            if (!m.Success) continue;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                RuleId = "WEB106", IssueType = "TodoComment",
                Title = $"{m.Groups[1].Value} comment",
                Description = $"Outstanding {m.Groups[1].Value}: {lines[i].Trim()}",
                Recommendation = "Resolve or create a backlog item and remove the comment.",
                LineNumber = i + 1, Severity = Severity.Low, Category = "Maintainability",
                CodeSnippet = lines[i].Trim()
            };
        }
    }
}

// WEB107 – Long function (>50 lines)
file sealed class LongFunctionRule : ILanguageRule
{
    private const int Threshold = 50;
    public string   RuleId          => "WEB107";
    public string   Name            => "Long function";
    public string   Description     => $"Function body exceeds {Threshold} lines.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex FuncStartRx = new(
        @"(?:function\s+[\w$]+|(?:const|let|var)\s+[\w$]+\s*=\s*(?:async\s+)?(?:\(.*?\)|[\w$]+)\s*=>)\s*\{",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        foreach (Match m in FuncStartRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var startLine = content[..m.Index].Count(c => c == '\n');
            int depth = 0, endLine = startLine;
            for (int i = startLine; i < lines.Length; i++)
            {
                depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
                if (depth <= 0) { endLine = i; break; }
            }
            var len = endLine - startLine + 1;
            if (len <= Threshold) continue;
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                RuleId = "WEB107", IssueType = "LongFunction",
                Title = $"Long function ({len} lines)",
                Description = $"Function starting at line {startLine + 1} is {len} lines long.",
                Recommendation = "Extract smaller helper functions to improve readability and testability.",
                LineNumber = startLine + 1, EndLine = endLine + 1,
                Severity = Severity.Medium, Category = "Maintainability",
                CodeSnippet = lines[startLine].Trim()
            };
        }
    }
}

// WEB108 – Deeply nested callbacks (callback hell)
file sealed class DeepCallbackNestingRule : ILanguageRule
{
    private const int MaxDepth = 4;
    public string   RuleId          => "WEB108";
    public string   Name            => "Deep callback nesting";
    public string   Description     => $"Nesting depth exceeds {MaxDepth}. Possible callback hell.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines     = content.Split('\n');
        int depth     = 0;
        int maxSeen   = 0;
        int maxLine   = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
            if (depth > maxSeen) { maxSeen = depth; maxLine = i + 1; }
        }

        if (maxSeen > MaxDepth)
            yield return new WebIssueDto
            {
                FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                RuleId = "WEB108", IssueType = "DeepNesting",
                Title = $"Deep nesting (depth {maxSeen})",
                Description = $"Maximum nesting depth {maxSeen} reached at line {maxLine}.",
                Recommendation = "Flatten with async/await, Promises, or named callbacks.",
                LineNumber = maxLine, Severity = Severity.Medium, Category = "Maintainability"
            };
    }
}

// WEB109 – Missing 'use strict'
file sealed class NoStrictModeRule : ILanguageRule
{
    public string   RuleId          => "WEB109";
    public string   Name            => "Missing 'use strict'";
    public string   Description     => "Non-module JS files should declare 'use strict' to catch common mistakes.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        // Skip TypeScript, ES modules (import/export), and files that already have strict
        if (filePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)) yield break;

        if (content.Contains("\"use strict\"") || content.Contains("'use strict'")) yield break;
        if (Regex.IsMatch(content, @"^\s*(import|export)\s", RegexOptions.Multiline)) yield break;  // ES module

        yield return new WebIssueDto
        {
            FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
            RuleId = "WEB109", IssueType = "NoStrictMode",
            Title = "Missing 'use strict'",
            Description = "Script does not opt into strict mode.",
            Recommendation = "Add \"use strict\"; at the top, or convert to an ES module.",
            LineNumber = 1, Severity = Severity.Low, Category = "Best practice",
            IsAutoFixable = true, FixedCode = "\"use strict\";\n" + content
        };
    }
}

// WEB110 – Hardcoded URLs / hostnames
file sealed class HardcodedUrlRule : ILanguageRule
{
    public string   RuleId          => "WEB110";
    public string   Name            => "Hardcoded URL";
    public string   Description     => "Absolute URLs or IP addresses hardcoded in source are fragile.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex UrlRx = new(
        @"[""'`](https?://[^""'`\s]{8,}|http://localhost:[0-9]+)[""'`]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            foreach (Match m in UrlRx.Matches(lines[i]))
                yield return new WebIssueDto
                {
                    FilePath = filePath, FileName = Path.GetFileName(filePath), Language = "JavaScript",
                    RuleId = "WEB110", IssueType = "HardcodedUrl",
                    Title = "Hardcoded URL",
                    Description = $"URL '{m.Value.Trim('\''  , '"', '`')}' is hardcoded.",
                    Recommendation = "Move to environment variables, config files or API constants.",
                    LineNumber = i + 1, Severity = Severity.Medium, Category = "Configuration",
                    CodeSnippet = lines[i].Trim()
                };
        }
    }
}
