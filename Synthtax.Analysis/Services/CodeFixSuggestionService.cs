using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Services;

public static class CodeFixSuggestionService
{
    public static (string description, string fixedCode, bool autoFixable) ForDeadVariable(
        string variableName, string codeSnippet, string declarationLine)
    {
        var description =
            $"Variable '{variableName}' is declared but never read. " +
            "Remove the declaration to reduce noise and avoid misleading readers.";
        var fixedCode = GenerateDeadVariableFix(variableName, declarationLine);
        return (description, fixedCode, autoFixable: true);
    }

    private static string GenerateDeadVariableFix(string varName, string line)
    {
        var trimmed = line.Trim();
        bool hasMethodCall = trimmed.Contains('(') && trimmed.Contains(')');
        bool hasAwait      = trimmed.TrimStart().StartsWith("await ");

        if (hasMethodCall || hasAwait)
        {
            var replaced = System.Text.RegularExpressions.Regex.Replace(
                trimmed,
                @"(?:var|[A-Za-z\[\]<>?,\s]+)\s+" +
                System.Text.RegularExpressions.Regex.Escape(varName) +
                @"\s*=\s*",
                "_ = ");
            return replaced;
        }
        return string.Empty;
    }

    public static (string description, string fixedCode, bool autoFixable) ForSqlInjection(
        string originalCode, string methodName, bool isInterpolation)
    {
        var description = isInterpolation
            ? $"SQL interpolated string in '{methodName}' allows injection. Use parameterized queries."
            : $"SQL string concatenation in '{methodName}' allows injection. Use parameterized queries.";

        var fixedCode =
            $"{originalCode.Trim()}\n" +
            "// Parameterized alternative:\n" +
            "var cmd = new SqlCommand(\"SELECT * FROM Users WHERE Id = @id\", connection);\n" +
            "cmd.Parameters.AddWithValue(\"@id\", userId);\n" +
            "// Or via EF Core:\n" +
            "var users = context.Users.Where(u => u.Id == userId).ToList();";

        return (description, fixedCode, autoFixable: false);
    }

    public static (string description, string fixedCode, bool autoFixable) ForMissingCancellationToken(
        string methodSignature, string methodName, List<string> innerCallsThatAcceptCt)
    {
        var description =
            $"Async method '{methodName}' does not accept a CancellationToken. " +
            "This prevents callers from cancelling the operation, which can cause resource leaks.";

        var fixedCode = GenerateCancellationTokenFix(methodSignature, innerCallsThatAcceptCt);
        return (description, fixedCode, autoFixable: false);
    }

    private static string GenerateCancellationTokenFix(string signature, List<string> innerCalls)
    {
        string newSignature;
        if (signature.Contains("()"))
            newSignature = signature.Replace("()", "(CancellationToken cancellationToken = default)");
        else if (signature.TrimEnd().EndsWith(')'))
            newSignature = signature.TrimEnd()[..^1] + ", CancellationToken cancellationToken = default)";
        else
            newSignature = signature + " // Add: CancellationToken cancellationToken = default";

        var propagation = innerCalls.Count > 0
            ? string.Join("\n", innerCalls.Select(c => $"// Propagate: {c} → pass cancellationToken"))
            : "// No inner async calls detected to propagate to.";

        return $"{signature}\n// Suggested fix:\n{newSignature}\n{propagation}";
    }

    public static (string description, string fixedCode, bool autoFixable) ForCognitiveComplexity(
        string methodName, int complexity, int threshold, string codeSnippet)
    {
        var description =
            $"Method '{methodName}' has a cognitive complexity of {complexity} " +
            $"(threshold: {threshold}). This makes it hard to understand and test.";

        var fixedCode = GenerateCognitiveComplexityAdvice(methodName, complexity, threshold, codeSnippet);
        return (description, fixedCode, autoFixable: false);
    }

    private static string GenerateCognitiveComplexityAdvice(
        string methodName, int complexity, int threshold, string snippet)
    {
        var tips = new List<string>();
        if (snippet.Contains("if") && snippet.Contains("else"))
            tips.Add("• Extract nested if/else branches into private helper methods with descriptive names.");
        if (snippet.Contains("foreach") || snippet.Contains("for "))
            tips.Add("• Extract loop bodies into separate methods (e.g., ProcessItem, HandleEntry).");
        if (snippet.Contains("switch"))
            tips.Add("• Replace switch statements with polymorphism or a Strategy pattern.");
        if (snippet.Contains("&&") || snippet.Contains("||"))
            tips.Add("• Extract complex boolean conditions into named boolean methods (e.g., IsEligible()).");

        tips.Add("• Apply the Single Responsibility Principle: each method should do one thing.");
        tips.Add($"• Target: split '{methodName}' into 2–3 methods with complexity ≤ {Math.Max(5, threshold / 2)} each.");

        return string.Join("\n", tips);
    }

    public static (string description, string fixedCode, bool autoFixable) ForAsyncVoid(
        string methodSignature)
    {
        var description =
            "async void method will swallow exceptions silently. " +
            "Use async Task instead so callers can observe exceptions and await completion.";
        var fixedCode = methodSignature.Replace("async void", "async Task");
        return (description, $"// Before:\n{methodSignature}\n// After:\n{fixedCode}", autoFixable: true);
    }

    public static (string description, string fixedCode, bool autoFixable) ForDotResultOrWait(
        string callSite, string callerMethodName)
    {
        var description =
            $"Calling .Result or .Wait() on a Task in '{callerMethodName}' can cause deadlocks " +
            "in ASP.NET Core. Await the Task instead.";

        return (description, $"{callSite}\n// Suggested fix:\nawait SomeAsyncCall();", autoFixable: false);
    }

    public static string ExtractCodeSnippet(
        string sourceText, int startLine, int endLine, int contextLines = 2)
    {
        var lines = sourceText.Split('\n');
        var from  = Math.Max(0, startLine - 1 - contextLines);
        var to    = Math.Min(lines.Length - 1, endLine - 1 + contextLines);
        var sb    = new System.Text.StringBuilder();

        for (int i = from; i <= to; i++)
        {
            var lineNo = i + 1;
            var marker = (lineNo >= startLine && lineNo <= endLine) ? ">>>" : "   ";
            sb.AppendLine($"{marker} {lineNo,4}: {lines[i]}");
        }
        return sb.ToString();
    }

    public static Severity MapComplexityToSeverity(int complexity) => complexity switch
    {
        > 30 => Severity.Critical,
        > 20 => Severity.High,
        > 15 => Severity.Medium,
        _    => Severity.Low
    };
}
