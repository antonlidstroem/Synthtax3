using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Languages.Python;

// ══════════════════════════════════════════════════════════════════════════════
// Python Plugin  (regex-based — no AST/interpreter dependency)
// ══════════════════════════════════════════════════════════════════════════════

public class PythonPlugin : Plugins.LanguagePluginBase
{
    public override string Language { get; } = "Python";
    public override string Version  { get; } = "1.0.0";
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".py", ".pyw"];

    public override IReadOnlyList<ILanguageRule> Rules { get; }

    public PythonPlugin(ILogger<PythonPlugin> logger) : base(logger)
    {
        Rules = new List<ILanguageRule>
        {
            new PyPrintStatementRule(),
            new PyBareExceptRule(),
            new PyMutableDefaultArgRule(),
            new PyWildcardImportRule(),
            new PyNoTypeHintRule(),
            new PyComparisonToNoneRule(),
            new PyGlobalKeywordRule(),
            new PyMagicNumberRule(),
            new PyTodoCommentRule(),
            new PyLongFunctionRule(),
            new PyUnusedImportRule(),
            new PyHardcodedPathRule()
        }.AsReadOnly();
    }
}

// ── Shared factory ────────────────────────────────────────────────────────────

internal static class PyIssueFactory
{
    public static WebIssueDto Make(
        string filePath, int line,
        string ruleId, string issueType,
        string title, string description, Severity severity, string category,
        string? snippet = null, string? recommendation = null,
        bool autoFix = false, string? fixedCode = null) => new()
    {
        FilePath       = filePath,
        FileName       = Path.GetFileName(filePath),
        Language       = "Python",
        RuleId         = ruleId,
        IssueType      = issueType,
        Title          = title,
        Description    = description,
        Recommendation = recommendation,
        LineNumber     = line,
        CodeSnippet    = snippet?.Trim(),
        Severity       = severity,
        Category       = category,
        IsAutoFixable  = autoFix,
        FixedCode      = fixedCode
    };
}

// ─── Rule PY001: print() in production ───────────────────────────────────────

public sealed class PyPrintStatementRule : ILanguageRule
{
    public string   RuleId          => "PY001";
    public string   Name            => "print() in production";
    public string   Description     => "print() calls should be replaced with the logging module in production code.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"^\s*print\s*\(", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            if (Rx.IsMatch(lines[i]))
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY001", "PrintStatement",
                    "print() in production code",
                    "print() is not configurable, has no log levels and pollutes stdout.",
                    Severity.Low, "Code quality", lines[i].Trim(),
                    "Use logging.debug/info/warning/error instead of print()");
        }
    }
}

// ─── Rule PY002: Bare except ─────────────────────────────────────────────────

public sealed class PyBareExceptRule : ILanguageRule
{
    public string   RuleId          => "PY002";
    public string   Name            => "Bare except clause";
    public string   Description     => "except: without an exception type catches ALL exceptions including SystemExit and KeyboardInterrupt.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"^\s*except\s*:", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            if (Rx.IsMatch(lines[i]))
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY002", "BareExcept",
                    "Bare except clause",
                    "Bare except catches SystemExit, KeyboardInterrupt and GeneratorExit — this can hide serious errors and prevent proper process termination.",
                    Severity.High, "Error handling", lines[i].Trim(),
                    "Use except Exception as e: or a specific exception type");
        }
    }
}

// ─── Rule PY003: Mutable default argument ────────────────────────────────────

public sealed class PyMutableDefaultArgRule : ILanguageRule
{
    public string   RuleId          => "PY003";
    public string   Name            => "Mutable default argument";
    public string   Description     => "Mutable defaults (list, dict, set) are shared across all calls. Classic Python gotcha.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    // def foo(x, items=[]):  or  def foo(x, d={}):  or  def foo(x, s=set()):
    private static readonly Regex Rx = new(
        @"def\s+\w+\s*\([^)]*=\s*(\[\s*\]|\{\s*\}|set\s*\(\s*\)|dict\s*\(\s*\)|list\s*\(\s*\))",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            var m = Rx.Match(lines[i]);
            if (!m.Success) continue;
            yield return PyIssueFactory.Make(filePath, i + 1,
                "PY003", "MutableDefaultArg",
                $"Mutable default argument: {m.Groups[1].Value}",
                $"The {m.Groups[1].Value} object is created once at function definition and shared across all calls. Mutations in one call affect all subsequent calls.",
                Severity.High, "Correctness", lines[i].Trim(),
                "Use None as default and create the object inside the function:\n  def foo(items=None):\n      if items is None: items = []",
                autoFix: false);
        }
    }
}

// ─── Rule PY004: Wildcard import ─────────────────────────────────────────────

public sealed class PyWildcardImportRule : ILanguageRule
{
    public string   RuleId          => "PY004";
    public string   Name            => "Wildcard import";
    public string   Description     => "from module import * pollutes the namespace and makes it impossible to trace where names come from.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"^\s*from\s+\S+\s+import\s+\*", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (Rx.IsMatch(lines[i]))
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY004", "WildcardImport",
                    "Wildcard import (import *)",
                    "Namespace pollution makes code hard to read and can cause silent name shadowing.",
                    Severity.Medium, "Maintainability", lines[i].Trim(),
                    "Import only what you need: from module import SpecificName");
        }
    }
}

// ─── Rule PY005: Missing type hints on public functions ──────────────────────

public sealed class PyNoTypeHintRule : ILanguageRule
{
    public string   RuleId          => "PY005";
    public string   Name            => "Missing type hints";
    public string   Description     => "Public functions without type annotations are harder to understand and debug.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    // Matches public functions (not starting with _) that have no -> return hint
    private static readonly Regex DefRx = new(
        @"^def\s+(?!_)(\w+)\s*\(([^)]*)\)\s*:", RegexOptions.Compiled);
    private static readonly Regex HintRx = new(
        @":\s*[\w\[\],. |]+", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            var m = DefRx.Match(lines[i]);
            if (!m.Success) continue;

            bool hasReturn = lines[i].Contains("->");
            bool hasParamHints = m.Groups[2].Value.Contains(':');

            if (!hasReturn && !hasParamHints)
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY005", "NoTypeHints",
                    $"Function '{m.Groups[1].Value}' lacks type hints",
                    "No parameter types or return type annotated. Type hints enable static analysis (mypy) and improve IDE support.",
                    Severity.Low, "Documentation", lines[i].Trim(),
                    "Add hints: def foo(x: int, y: str) -> bool:");
        }
    }
}

// ─── Rule PY006: Comparison to None/True/False with == ──────────────────────

public sealed class PyComparisonToNoneRule : ILanguageRule
{
    public string   RuleId          => "PY006";
    public string   Name            => "Comparison with == None / == True / == False";
    public string   Description     => "PEP 8 mandates 'is None', 'is True', 'is False' for singleton comparisons.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"==\s*(None|True|False)|(?:None|True|False)\s*==",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            var m = Rx.Match(lines[i]);
            if (m.Success)
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY006", "ComparisonToNone",
                    $"Compare with 'is' not '=='",
                    $"PEP 8: comparison to None/True/False should use 'is' or 'is not', not '=='.",
                    Severity.Low, "Code style", lines[i].Trim(),
                    "Use: if x is None: / if x is True: / if not x:",
                    autoFix: true,
                    fixedCode: Regex.Replace(lines[i],
                        @"==\s*(None|True|False)",
                        m2 => $"is {m2.Groups[1].Value}"));
        }
    }
}

// ─── Rule PY007: global keyword ──────────────────────────────────────────────

public sealed class PyGlobalKeywordRule : ILanguageRule
{
    public string   RuleId          => "PY007";
    public string   Name            => "global keyword usage";
    public string   Description     => "global creates hidden coupling between functions and module-level state.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"^\s*global\s+\w+", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            if (Rx.IsMatch(lines[i]))
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY007", "GlobalKeyword",
                    "global keyword",
                    "global creates implicit shared state, making code hard to test and reason about.",
                    Severity.Medium, "Design", lines[i].Trim(),
                    "Pass state explicitly as function parameters, or encapsulate it in a class");
        }
    }
}

// ─── Rule PY008: Magic numbers ───────────────────────────────────────────────

public sealed class PyMagicNumberRule : ILanguageRule
{
    public string   RuleId          => "PY008";
    public string   Name            => "Magic number";
    public string   Description     => "Numeric literals should be extracted to named constants.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx      = new(@"\b(\d{2,})\b", RegexOptions.Compiled);
    private static readonly HashSet<int> OkValues = [0, 1, 2, 10, 16, 32, 64, 100, 256, 1000, 1024];

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var trimmed = lines[i].TrimStart();
            // Skip comment lines, imports, constants (UPPERCASE = N)
            if (PyHelpers.IsComment(trimmed) || trimmed.StartsWith("import") || trimmed.StartsWith("from")
                || Regex.IsMatch(trimmed, @"^[A-Z_]+\s*=")) continue;

            foreach (Match m in Rx.Matches(lines[i]))
            {
                if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
                if (OkValues.Contains(n)) continue;
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY008", "MagicNumber",
                    $"Magic number: {n}",
                    $"The literal {n} has no self-documenting name.",
                    Severity.Low, "Maintainability", lines[i].Trim(),
                    $"MY_CONSTANT = {n}  # at module level");
            }
        }
    }
}

// ─── Rule PY009: TODO/FIXME ───────────────────────────────────────────────────

public sealed class PyTodoCommentRule : ILanguageRule
{
    public string   RuleId          => "PY009";
    public string   Name            => "TODO/FIXME comment";
    public string   Description     => "Outstanding work items should be tracked in an issue tracker.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"#.*\b(TODO|FIXME|HACK|XXX)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = Rx.Match(lines[i]);
            if (m.Success)
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY009", "TodoComment",
                    $"{m.Groups[1].Value} comment",
                    lines[i].Trim(),
                    Severity.Low, "Maintainability", lines[i].Trim(),
                    "Create a GitHub/Jira issue and remove the comment");
        }
    }
}

// ─── Rule PY010: Long function ────────────────────────────────────────────────

public sealed class PyLongFunctionRule : ILanguageRule
{
    private const int Threshold = 50;

    public string   RuleId          => "PY010";
    public string   Name            => $"Long function (>{Threshold} lines)";
    public string   Description     => $"Functions over {Threshold} lines are hard to read and test.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex DefRx = new(@"^def\s+\w+|^    def\s+\w+", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!DefRx.IsMatch(lines[i])) continue;

            var indent     = lines[i].Length - lines[i].TrimStart().Length;
            var startLine  = i;
            var bodyIndent = indent + 4;  // Python indentation

            int end = i + 1;
            while (end < lines.Length)
            {
                var l = lines[end];
                if (!string.IsNullOrWhiteSpace(l))
                {
                    var li = l.Length - l.TrimStart().Length;
                    if (li <= indent && !PyHelpers.IsComment(l)) break;
                }
                end++;
            }

            var len = end - startLine;
            if (len <= Threshold) continue;

            yield return PyIssueFactory.Make(filePath, startLine + 1,
                "PY010", "LongFunction",
                $"Long function ({len} lines)",
                $"Function at line {startLine + 1} is {len} lines long.",
                Severity.Medium, "Maintainability", lines[startLine].Trim(),
                "Extract helper functions. Aim for <30 lines per function.");
        }
    }
}

// ─── Rule PY011: Unused imports (heuristic) ──────────────────────────────────

public sealed class PyUnusedImportRule : ILanguageRule
{
    public string   RuleId          => "PY011";
    public string   Name            => "Possibly unused import";
    public string   Description     => "Import statement for a name that does not appear anywhere else in the file.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex ImportRx = new(
        @"^(?:import\s+(\S+)|from\s+\S+\s+import\s+(\w+))",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        foreach (Match m in ImportRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var importedName = !string.IsNullOrEmpty(m.Groups[1].Value)
                ? m.Groups[1].Value.Split('.')[0]
                : m.Groups[2].Value;

            if (string.IsNullOrEmpty(importedName)) continue;

            // Count occurrences outside the import line itself
            var outside = content.Replace(m.Value, string.Empty);
            var count   = Regex.Matches(outside, $@"\b{Regex.Escape(importedName)}\b").Count;

            if (count == 0)
            {
                var line = content[..m.Index].Count(c => c == '\n') + 1;
                yield return PyIssueFactory.Make(filePath, line,
                    "PY011", "UnusedImport",
                    $"Possibly unused import '{importedName}'",
                    $"'{importedName}' is imported but does not appear anywhere else in the file.",
                    Severity.Low, "Cleanliness", m.Value.Trim(),
                    "Remove the import, or use noqa: F401 if intentional");
            }
        }
    }
}

// ─── Rule PY012: Hardcoded file system paths ──────────────────────────────────

public sealed class PyHardcodedPathRule : ILanguageRule
{
    public string   RuleId          => "PY012";
    public string   Name            => "Hardcoded file path";
    public string   Description     => "Hardcoded absolute paths break portability across environments.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"[""'](/[a-zA-Z][^""']*|[A-Za-z]:\\[^""']+)[""']",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (PyHelpers.IsComment(lines[i])) continue;
            foreach (Match m in Rx.Matches(lines[i]))
                yield return PyIssueFactory.Make(filePath, i + 1,
                    "PY012", "HardcodedPath",
                    "Hardcoded file path",
                    $"Path '{m.Value}' is hardcoded and won't work on other machines or in containers.",
                    Severity.Medium, "Configuration", lines[i].Trim(),
                    "Use os.environ, pathlib.Path(__file__).parent, or a config file instead");
        }
    }
}

// ─── Shared helpers ───────────────────────────────────────────────────────────

internal static class PyHelpers
{
    public static bool IsComment(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith('#') || t.StartsWith("\"\"\"") || t.StartsWith("'''");
    }
}
