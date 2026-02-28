using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Languages.Java;

// ══════════════════════════════════════════════════════════════════════════════
// Java Plugin  (regex-based — no JDT/LSP dependency needed)
// ══════════════════════════════════════════════════════════════════════════════

public class JavaPlugin : Plugins.LanguagePluginBase
{
    public override string Language { get; } = "Java";
    public override string Version  { get; } = "1.0.0";
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".java"];

    public override IReadOnlyList<ILanguageRule> Rules { get; }

    public JavaPlugin(ILogger<JavaPlugin> logger) : base(logger)
    {
        Rules = new List<ILanguageRule>
        {
            new JavaSystemOutRule(),
            new JavaRawTypeRule(),
            new JavaEqualsHashCodeRule(),
            new JavaStringDoubleEqualsRule(),
            new JavaEmptyCatchRule(),
            new JavaMagicNumberRule(),
            new JavaTodoCommentRule(),
            new JavaDeprecatedNoDocRule(),
            new JavaNullReturnPublicRule(),
            new JavaLongMethodRule(),
            new JavaMutablePublicFieldRule(),
            new JavaThreadSafetyRule()
        }.AsReadOnly();
    }
}

// ── Shared factory ────────────────────────────────────────────────────────────

internal static class JavaIssueFactory
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
        Language       = "Java",
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

// ─── Rule JAVA001: System.out.println ────────────────────────────────────────

public sealed class JavaSystemOutRule : ILanguageRule
{
    public string   RuleId          => "JAVA001";
    public string   Name            => "System.out.println";
    public string   Description     => "System.out/err print calls should not be committed in production code.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"\bSystem\.(out|err)\.(print|println|printf|format)\s*\(",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (lines[i].TrimStart().StartsWith("//")) continue;
            if (Rx.IsMatch(lines[i]))
                yield return JavaIssueFactory.Make(filePath, i + 1,
                    "JAVA001", "SystemOutPrintln",
                    "System.out/err print in production",
                    "Debug output committed to source. Use a logging framework (SLF4J, Log4j2).",
                    Severity.Low, "Code quality", lines[i].Trim(),
                    "Replace with: logger.debug(\"...\") or logger.info(\"...\")");
        }
    }
}

// ─── Rule JAVA002: Raw generic type ──────────────────────────────────────────

public sealed class JavaRawTypeRule : ILanguageRule
{
    public string   RuleId          => "JAVA002";
    public string   Name            => "Raw generic type";
    public string   Description     => "Using raw types (e.g. List, Map without <T>) bypasses compile-time type safety.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"(?<!\w)(List|Map|Set|Collection|ArrayList|HashMap|HashSet|LinkedList|Queue|Deque)\s+[a-zA-Z_]\w*\s*[=;(,]",
        RegexOptions.Compiled);
    private static readonly Regex HasGeneric = new(@"<[^>]+>", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var line = lines[i];
            if (line.TrimStart().StartsWith("//")) continue;
            var m = Rx.Match(line);
            if (!m.Success) continue;
            if (HasGeneric.IsMatch(line[..m.Index + m.Groups[1].Length + 2])) continue;
            yield return JavaIssueFactory.Make(filePath, i + 1,
                "JAVA002", "RawType",
                $"Raw type '{m.Groups[1].Value}'",
                $"'{m.Groups[1].Value}' used without type parameter. The compiler cannot catch type errors.",
                Severity.Medium, "Type safety", line.Trim(),
                $"Use {m.Groups[1].Value}<YourType> to enable type safety");
        }
    }
}

// ─── Rule JAVA003: equals() without hashCode() ───────────────────────────────

public sealed class JavaEqualsHashCodeRule : ILanguageRule
{
    public string   RuleId          => "JAVA003";
    public string   Name            => "equals() without hashCode()";
    public string   Description     => "Violates the Java equals/hashCode contract; breaks HashMap/HashSet behaviour.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex EqualsRx  = new(@"public\s+boolean\s+equals\s*\(", RegexOptions.Compiled);
    private static readonly Regex HashRx    = new(@"public\s+int\s+hashCode\s*\(",    RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        if (!EqualsRx.IsMatch(content) || HashRx.IsMatch(content)) yield break;
        var line = content.Split('\n').Select((l, i) => (l, i))
            .FirstOrDefault(x => EqualsRx.IsMatch(x.l)).i + 1;
        yield return JavaIssueFactory.Make(filePath, line,
            "JAVA003", "EqualsWithoutHashCode",
            "equals() overridden without hashCode()",
            "Objects that are equal must have equal hash codes. Violating this causes silent HashMap/HashSet bugs.",
            Severity.High, "Correctness", null,
            "Generate hashCode() using Objects.hash(field1, field2, ...)");
    }
}

// ─── Rule JAVA004: String compared with == ───────────────────────────────────

public sealed class JavaStringDoubleEqualsRule : ILanguageRule
{
    public string   RuleId          => "JAVA004";
    public string   Name            => "String compared with ==";
    public string   Description     => "== tests reference equality, not value equality for Strings.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"[\w.]+\s*==\s*""[^""]*""|""[^""]*""\s*==\s*[\w.]+",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (lines[i].TrimStart().StartsWith("//")) continue;
            if (Rx.IsMatch(lines[i]))
                yield return JavaIssueFactory.Make(filePath, i + 1,
                    "JAVA004", "StringDoubleEquals",
                    "String compared with ==",
                    "== compares object references. Two new String(\"x\") objects are NOT == even with equal content.",
                    Severity.High, "Correctness", lines[i].Trim(),
                    "Use .equals() or Objects.equals(a, b) for null-safe value comparison");
        }
    }
}

// ─── Rule JAVA005: Empty catch block ─────────────────────────────────────────

public sealed class JavaEmptyCatchRule : ILanguageRule
{
    public string   RuleId          => "JAVA005";
    public string   Name            => "Empty catch block";
    public string   Description     => "Silently swallowed exceptions hide bugs and make diagnosis impossible.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"catch\s*\([^)]+\)\s*\{\s*\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in Rx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return JavaIssueFactory.Make(filePath, line,
                "JAVA005", "EmptyCatch",
                "Empty catch block",
                "Exception is caught and silently discarded. Bugs become invisible.",
                Severity.High, "Error handling", m.Value.Trim(),
                "At minimum: logger.error(\"Unexpected error in ...\", e)");
        }
    }
}

// ─── Rule JAVA006: Magic numbers ─────────────────────────────────────────────

public sealed class JavaMagicNumberRule : ILanguageRule
{
    public string   RuleId          => "JAVA006";
    public string   Name            => "Magic number";
    public string   Description     => "Numeric literals (other than 0/1/-1) should be named constants.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(@"\b(\d{2,})\b", RegexOptions.Compiled);

    private static readonly HashSet<int> AllowedValues = [0, 1, 2, 10, 16, 32, 64, 100, 256, 1000, 1024];

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("*") ||
                trimmed.StartsWith("import") || trimmed.StartsWith("package") ||
                trimmed.StartsWith("@") || trimmed.Contains("static final")) continue;

            foreach (Match m in Rx.Matches(lines[i]))
            {
                if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
                if (AllowedValues.Contains(n)) continue;
                yield return JavaIssueFactory.Make(filePath, i + 1,
                    "JAVA006", "MagicNumber",
                    $"Magic number: {n}",
                    $"The literal {n} has no self-documenting name. Extract to a named constant.",
                    Severity.Low, "Maintainability", lines[i].Trim(),
                    $"private static final int DESCRIPTIVE_NAME = {n};");
            }
        }
    }
}

// ─── Rule JAVA007: TODO/FIXME ─────────────────────────────────────────────────

public sealed class JavaTodoCommentRule : ILanguageRule
{
    public string   RuleId          => "JAVA007";
    public string   Name            => "TODO/FIXME comment";
    public string   Description     => "Outstanding work items should be tracked in an issue tracker.";
    public Severity DefaultSeverity => Severity.Low;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"//.*\b(TODO|FIXME|HACK|XXX)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = Rx.Match(lines[i]);
            if (m.Success)
                yield return JavaIssueFactory.Make(filePath, i + 1,
                    "JAVA007", "TodoComment",
                    $"{m.Groups[1].Value} comment",
                    lines[i].Trim(),
                    Severity.Low, "Maintainability", lines[i].Trim(),
                    "Create a Jira/GitHub issue and remove the inline comment");
        }
    }
}

// ─── Rule JAVA008: @Deprecated without Javadoc ───────────────────────────────

public sealed class JavaDeprecatedNoDocRule : ILanguageRule
{
    public string   RuleId          => "JAVA008";
    public string   Name            => "@Deprecated without Javadoc";
    public string   Description     => "@Deprecated should document why and what to use instead.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex AnnotationRx = new(@"@Deprecated", RegexOptions.Compiled);
    private static readonly Regex DocTagRx     = new(@"@deprecated", RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in AnnotationRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var before = content[..m.Index];
            if (DocTagRx.IsMatch(before[Math.Max(0, before.Length - 500)..]))
                continue;
            var line = before.Count(c => c == '\n') + 1;
            yield return JavaIssueFactory.Make(filePath, line,
                "JAVA008", "DeprecatedNoDoc",
                "@Deprecated without @deprecated Javadoc",
                "Callers have no guidance on what to use instead or why this is deprecated.",
                Severity.Medium, "Documentation", "@Deprecated",
                "Add /** @deprecated Reason. Use {@link NewClass#newMethod()} instead. */ before the element");
        }
    }
}

// ─── Rule JAVA009: Public method returns null ─────────────────────────────────

public sealed class JavaNullReturnPublicRule : ILanguageRule
{
    public string   RuleId          => "JAVA009";
    public string   Name            => "Public method returns null";
    public string   Description     => "Returning null forces callers to null-check. Use Optional<T> instead.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex PublicMethodRx = new(
        @"public\s+\w[\w<>, \[\]]*\s+\w+\s*\(", RegexOptions.Compiled);
    private static readonly Regex ReturnNullRx   = new(
        @"\breturn\s+null\s*;",                   RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!ReturnNullRx.IsMatch(lines[i])) continue;
            var lookback = lines[Math.Max(0, i - 50)..i];
            if (!lookback.Any(l => PublicMethodRx.IsMatch(l))) continue;
            yield return JavaIssueFactory.Make(filePath, i + 1,
                "JAVA009", "NullReturnPublicMethod",
                "Public method returns null",
                "Null return causes NullPointerException risk at call sites.",
                Severity.Medium, "Null safety", lines[i].Trim(),
                "Return Optional.empty() instead, or throw a meaningful exception");
        }
    }
}

// ─── Rule JAVA010: Long method ────────────────────────────────────────────────

public sealed class JavaLongMethodRule : ILanguageRule
{
    private const int Threshold = 60;

    public string   RuleId          => "JAVA010";
    public string   Name            => $"Long method (>{Threshold} lines)";
    public string   Description     => "Methods over 60 lines are hard to read, test and refactor.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex MethodStartRx = new(
        @"(?:public|protected|private|static|\s)+[\w<>\[\]]+\s+\w+\s*\([^)]*\)\s*(?:throws\s+[\w,\s]+)?\s*\{",
        RegexOptions.Compiled);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        var lines = content.Split('\n');
        foreach (Match m in MethodStartRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var start = content[..m.Index].Count(c => c == '\n');
            int depth = 0, end = start;
            for (int i = start; i < lines.Length; i++)
            {
                depth += lines[i].Count(c => c == '{') - lines[i].Count(c => c == '}');
                if (depth <= 0) { end = i; break; }
            }
            var len = end - start + 1;
            if (len <= Threshold) continue;
            yield return JavaIssueFactory.Make(filePath, start + 1,
                "JAVA010", "LongMethod",
                $"Long method ({len} lines)",
                $"Method at line {start + 1} is {len} lines long.",
                len > Threshold * 2 ? Severity.High : Severity.Medium,
                "Maintainability", lines[start].Trim(),
                "Extract sub-tasks into private helper methods (Single Responsibility Principle)");
        }
    }
}

// ─── Rule JAVA011: Mutable public field ──────────────────────────────────────

public sealed class JavaMutablePublicFieldRule : ILanguageRule
{
    public string   RuleId          => "JAVA011";
    public string   Name            => "Mutable public field";
    public string   Description     => "Public non-final fields break encapsulation.";
    public Severity DefaultSeverity => Severity.Medium;
    public bool     IsEnabled       => true;

    private static readonly Regex Rx = new(
        @"^\s*public\s+(?!final\s)(?!static\s+final\s)(?!enum\s)(?!class\s)(?!interface\s)[\w<>\[\]]+\s+\w+\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        foreach (Match m in Rx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return JavaIssueFactory.Make(filePath, line,
                "JAVA011", "MutablePublicField",
                "Mutable public field",
                "Any class can modify this field without validation.",
                Severity.Medium, "Encapsulation", m.Value.Trim(),
                "Make private and expose via getters/setters, or make public final");
        }
    }
}

// ─── Rule JAVA012: Thread safety ─────────────────────────────────────────────

public sealed class JavaThreadSafetyRule : ILanguageRule
{
    public string   RuleId          => "JAVA012";
    public string   Name            => "Possible thread-safety issue";
    public string   Description     => "Non-volatile, non-synchronized shared state in a threading context.";
    public Severity DefaultSeverity => Severity.High;
    public bool     IsEnabled       => true;

    private static readonly Regex ThreadContextRx = new(
        @"\b(Thread|Runnable|Callable|ExecutorService|CompletableFuture|@Async|ScheduledExecutorService|ForkJoinPool)\b",
        RegexOptions.Compiled);
    private static readonly Regex SharedFieldRx = new(
        @"^\s*private\s+(?!final\s)(?!volatile\s)(?!static\s+final\s)[\w<>\[\]]+\s+\w+",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public IEnumerable<WebIssueDto> Analyze(string content, string filePath, CancellationToken ct)
    {
        if (!ThreadContextRx.IsMatch(content)) yield break;
        if (content.Contains("@ThreadSafe") || content.Contains("@GuardedBy")) yield break;

        foreach (Match m in SharedFieldRx.Matches(content))
        {
            ct.ThrowIfCancellationRequested();
            var line = content[..m.Index].Count(c => c == '\n') + 1;
            yield return JavaIssueFactory.Make(filePath, line,
                "JAVA012", "ThreadSafety",
                "Possible thread-safety issue",
                "Non-final, non-volatile instance field in a class using threading constructs. Possible data race.",
                Severity.High, "Concurrency", m.Value.Trim(),
                "Use AtomicXxx, volatile, synchronized, or java.util.concurrent.* data structures");
        }
    }
}
