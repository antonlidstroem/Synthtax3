using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

/// <summary>
/// <b>SA003 — Complex Method Extraction Candidate</b>
///
/// <para>Identifierar metoder med hög cyklomatisk komplexitet och/eller
/// för många rader, och flaggar dem för uppdelning.</para>
///
/// <para><b>Triggerkriteria (valfri kombination):</b>
/// <list type="bullet">
///   <item>Cyklomatisk komplexitet ≥ <see cref="MaxCyclomaticComplexity"/> (default 10).</item>
///   <item>Antal rader ≥ <see cref="MaxMethodLines"/> (default 30).</item>
///   <item>Djup nästling ≥ <see cref="MaxNestingDepth"/> (default 4).</item>
/// </list>
/// </para>
///
/// <para><b>Undantag:</b>
/// <list type="bullet">
///   <item>Genererade metoder (attributen [GeneratedCode], [CompilerGenerated]).</item>
///   <item>Override-metoder med bara ett base()-anrop.</item>
///   <item>Tester-metoder (metodens klass ärver från TestBase eller har [TestClass]/[Fact]).</item>
/// </list>
/// </para>
/// </summary>
public sealed class ComplexMethodRule
{
    public const string RuleId = "SA003";

    // ── Konfigurerbara trösklar ────────────────────────────────────────────
    public int MaxCyclomaticComplexity { get; init; } = 10;
    public int MaxMethodLines          { get; init; } = 30;
    public int MaxNestingDepth         { get; init; } = 4;

    // Attribut som indikerar genererad kod
    private static readonly HashSet<string> GeneratedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "GeneratedCode", "CompilerGenerated", "DebuggerStepThrough",
        "System.CodeDom.Compiler.GeneratedCode"
    };

    // Attribut som indikerar testmetoder
    private static readonly HashSet<string> TestAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fact", "Theory", "Test", "TestMethod", "TestCase"
    };

    public IReadOnlyList<RawIssue> Analyze(
        SyntaxTree            tree,
        string                filePath,
        IReadOnlySet<string>? enabledRules = null)
    {
        if (enabledRules is not null && !enabledRules.Contains(RuleId)) return [];

        var root   = tree.GetRoot();
        var issues = new List<RawIssue>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (ShouldSkip(method)) continue;

            var metrics = ComputeMetrics(method);
            if (!metrics.ExceedsThresholds(this)) continue;

            var classNode = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var ns        = GetNamespace(method);
            var lineSpan  = tree.GetLineSpan(method.Span);

            issues.Add(new RawIssue
            {
                RuleId    = RuleId,
                Scope     = LogicalScope.ForMethod(ns, classNode?.Identifier.Text, method.Identifier.Text),
                FilePath  = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine   = lineSpan.EndLinePosition.Line   + 1,
                Snippet   = BuildSignatureSnippet(method),
                Message   = BuildMessage(method.Identifier.Text, metrics),
                Suggestion = BuildSuggestion(method, metrics),
                Severity  = ClassifySeverity(metrics),
                Category  = "Maintainability",
                IsAutoFixable = false,
                Metadata  = new Dictionary<string, string>
                {
                    ["cyclomaticComplexity"] = metrics.CyclomaticComplexity.ToString(),
                    ["lineCount"]            = metrics.LineCount.ToString(),
                    ["nestingDepth"]         = metrics.MaxNestingDepth.ToString(),
                    ["extractionHints"]      = string.Join("|", metrics.ExtractionHints)
                }
            });
        }

        return issues.AsReadOnly();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Metrics computation
    // ═══════════════════════════════════════════════════════════════════════

    private sealed record MethodMetrics(
        int          CyclomaticComplexity,
        int          LineCount,
        int          MaxNestingDepth,
        List<string> ExtractionHints)
    {
        public bool ExceedsThresholds(ComplexMethodRule rule) =>
            CyclomaticComplexity >= rule.MaxCyclomaticComplexity ||
            LineCount            >= rule.MaxMethodLines          ||
            MaxNestingDepth      >= rule.MaxNestingDepth;
    }

    private static MethodMetrics ComputeMetrics(MethodDeclarationSyntax method)
    {
        var cyclomatic    = ComputeCyclomaticComplexity(method);
        var lines         = method.ToString().Split('\n').Length;
        var maxDepth      = ComputeMaxNestingDepth(method);
        var hints         = IdentifyExtractionHints(method);

        return new MethodMetrics(cyclomatic, lines, maxDepth, hints);
    }

    /// <summary>
    /// Cyklomatisk komplexitet = 1 + antal beslutspunkter.
    /// Beslutspunkter: if, else if, while, for, foreach, case, &&, ||, ??, ?.
    /// </summary>
    private static int ComputeCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        int complexity = 1; // baseline

        foreach (var node in method.DescendantNodes())
        {
            complexity += node.Kind() switch
            {
                SyntaxKind.IfStatement            => 1,
                SyntaxKind.ElseClause             => 0, // else räknas inte separat
                SyntaxKind.WhileStatement         => 1,
                SyntaxKind.ForStatement           => 1,
                SyntaxKind.ForEachStatement       => 1,
                SyntaxKind.DoStatement            => 1,
                SyntaxKind.CaseSwitchLabel        => 1,
                SyntaxKind.WhenClause             => 1,
                SyntaxKind.ConditionalExpression  => 1, // ?:
                SyntaxKind.CoalesceExpression     => 1, // ??
                SyntaxKind.LogicalAndExpression   => 1, // &&
                SyntaxKind.LogicalOrExpression    => 1, // ||
                SyntaxKind.ConditionalAccessExpression => 1, // ?.
                SyntaxKind.SwitchExpressionArm   => 1,
                _ => 0
            };
        }

        return complexity;
    }

    private static int ComputeMaxNestingDepth(MethodDeclarationSyntax method)
    {
        int maxDepth = 0;

        void Visit(SyntaxNode node, int depth)
        {
            maxDepth = Math.Max(maxDepth, depth);
            foreach (var child in node.ChildNodes())
            {
                var addDepth = child.Kind() switch
                {
                    SyntaxKind.IfStatement         => 1,
                    SyntaxKind.ElseClause          => 1,
                    SyntaxKind.WhileStatement      => 1,
                    SyntaxKind.ForStatement        => 1,
                    SyntaxKind.ForEachStatement    => 1,
                    SyntaxKind.TryStatement        => 1,
                    SyntaxKind.Block               => 0, // blocket i sig ökar inte djupet
                    _                              => 0
                };
                Visit(child, depth + addDepth);
            }
        }

        Visit(method.Body ?? (SyntaxNode?)method.ExpressionBody ?? method, 0);
        return maxDepth;
    }

    /// <summary>
    /// Identifierar naturliga extraktionspunkter i metoden.
    /// Returnerar läsliga beskrivningar, t.ex. "Validation block (lines 5-12)".
    /// </summary>
    private static List<string> IdentifyExtractionHints(MethodDeclarationSyntax method)
    {
        var hints = new List<string>();

        // Validerings-block: sekvens av if-null-throw i börjane
        var body = method.Body;
        if (body != null)
        {
            var nullChecks = body.Statements
                .TakeWhile(s => IsNullCheckOrGuard(s))
                .ToList();
            if (nullChecks.Count >= 3)
                hints.Add($"Validation block ({nullChecks.Count} guard clauses) → Extract 'Validate{method.Identifier.Text}Parameters()'");

            // Loop-kropp
            var loops = body.Statements.OfType<ForEachStatementSyntax>()
                .Concat<StatementSyntax>(body.Statements.OfType<ForStatementSyntax>())
                .ToList();
            foreach (var loop in loops)
            {
                var loopLines = loop.ToString().Split('\n').Length;
                if (loopLines >= 10)
                    hints.Add($"Loop body ({loopLines} lines) → Extract inner loop logic to separate method");
            }

            // Try-catch-kropp
            var tries = body.Statements.OfType<TryStatementSyntax>().ToList();
            if (tries.Count >= 1 && tries[0].Block.Statements.Count >= 8)
                hints.Add("Large try block → Extract the try body into its own method");

            // Sektion med kommentar-separator
            foreach (var stmt in body.Statements)
            {
                var leadingTrivia = stmt.GetLeadingTrivia()
                    .Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia))
                    .Select(t => t.ToString())
                    .FirstOrDefault();

                if (leadingTrivia?.StartsWith("// ──") == true ||
                    leadingTrivia?.StartsWith("// Step") == true ||
                    leadingTrivia?.StartsWith("// Phase") == true)
                {
                    hints.Add($"Section '{leadingTrivia.Trim()}' → natural extraction point");
                }
            }
        }

        return hints;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private static bool ShouldSkip(MethodDeclarationSyntax method)
    {
        // Genererade metoder
        var attrs = method.AttributeLists
            .SelectMany(al => al.Attributes)
            .Select(a => a.Name.ToString());

        if (attrs.Any(a => GeneratedAttributes.Any(g =>
            a.Contains(g, StringComparison.OrdinalIgnoreCase))))
            return true;

        // Testmetoder
        if (attrs.Any(a => TestAttributes.Any(t =>
            a.Equals(t, StringComparison.OrdinalIgnoreCase))))
            return true;

        // Override med bara base()-anrop
        if (method.Modifiers.Any(m => m.Text == "override") &&
            method.Body?.Statements.Count <= 1)
            return true;

        // Konstruktorer och operatörer — ej metoder, men extra skydd
        return false;
    }

    private static bool IsNullCheckOrGuard(StatementSyntax stmt) =>
        stmt is IfStatementSyntax ifStmt &&
        ifStmt.Statement is BlockSyntax block &&
        block.Statements.Any(s => s is ThrowStatementSyntax or ReturnStatementSyntax);

    private static Severity ClassifySeverity(MethodMetrics m)
    {
        if (m.CyclomaticComplexity >= 20 || m.LineCount >= 60) return Severity.High;
        if (m.CyclomaticComplexity >= 15 || m.LineCount >= 45) return Severity.Medium;
        return Severity.Low;
    }

    private static string BuildMessage(string methodName, MethodMetrics metrics)
    {
        var parts = new List<string>();
        if (metrics.CyclomaticComplexity >= 10)
            parts.Add($"cyclomatic complexity {metrics.CyclomaticComplexity}");
        if (metrics.LineCount >= 30)
            parts.Add($"{metrics.LineCount} lines");
        if (metrics.MaxNestingDepth >= 4)
            parts.Add($"nesting depth {metrics.MaxNestingDepth}");

        return $"Method '{methodName}' has {string.Join(", ", parts)}. " +
               "Consider extracting sub-operations into smaller, single-purpose methods.";
    }

    private static string BuildSuggestion(MethodDeclarationSyntax method, MethodMetrics metrics)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Decompose '{method.Identifier.Text}' by extracting:");
        foreach (var hint in metrics.ExtractionHints.Take(3))
            sb.AppendLine($"  • {hint}");

        if (!metrics.ExtractionHints.Any())
            sb.AppendLine("  • Identify logical phases (validation, processing, output) and extract each to a private helper method.");

        return sb.ToString().TrimEnd();
    }

    private static string BuildSignatureSnippet(MethodDeclarationSyntax method)
    {
        // Visa bara signaturen + de första 5 raderna av kroppen
        var lines = method.ToString().Split('\n');
        var preview = lines.Length <= 8
            ? method.ToString()
            : string.Join('\n', lines.Take(8)) + "\n    // ... (" + (lines.Length - 8) + " more lines)";
        return preview;
    }

    private static string? GetNamespace(SyntaxNode node)
    {
        var nsDecl = node.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()
                     ?? (SyntaxNode?)node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        return nsDecl switch
        {
            NamespaceDeclarationSyntax ns => ns.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax fns => fns.Name.ToString(),
            _ => null
        };
    }
}
