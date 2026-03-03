using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

public sealed class MethodExtractionRule : ISynthtaxRule
{
    public static string RuleId => "SA003";
    string ISynthtaxRule.RuleId => RuleId;

    private const int MaxLines      = 30;
    private const int MaxComplexity = 10;
    private const int MaxNesting    = 4;

    // ── Interface implementation ──────────────────────────────────────────
    public IEnumerable<RawIssue> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct)
        => AnalyzeCore(root, filePath, ct);

    // ── Overload used by tests and CSharpStructuralPlugin ─────────────────
    public IEnumerable<RawIssue> Analyze(
        SyntaxTree tree, string filePath, IReadOnlySet<string>? enabledRuleIds = null)
    {
        if (enabledRuleIds is not null && !enabledRuleIds.Contains(RuleId))
            return [];
        return AnalyzeCore(tree.GetRoot(), filePath, CancellationToken.None);
    }

    // ── Static overload used by ArchitecturalRefactoringPlugin ────────────
    public static IEnumerable<RawIssue> Analyze(
        SyntaxNode root, string filePath, CancellationToken ct)
        => new MethodExtractionRule().AnalyzeCore(root, filePath, ct);

    // ── Core logic ────────────────────────────────────────────────────────
    private IEnumerable<RawIssue> AnalyzeCore(
        SyntaxNode root, string filePath, CancellationToken ct)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (method.Body == null && method.ExpressionBody == null) continue;

            // Skip generated methods
            if (method.AttributeLists.Any(al => al.Attributes.Any(a =>
                a.Name.ToString().Contains("GeneratedCode")))) continue;

            var lines      = method.ToString().Split('\n').Length;
            var complexity = ComputeComplexity(method);
            var nesting    = ComputeMaxNesting(method);

            if (lines <= MaxLines && complexity <= MaxComplexity && nesting <= MaxNesting) continue;

            var lineSpan = method.GetLocation().GetLineSpan();
            var cls      = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var ns       = method.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            var name     = method.Identifier.Text;

            var reasons = new List<string>();
            if (lines      > MaxLines)      reasons.Add($"{lines} lines");
            if (complexity > MaxComplexity) reasons.Add($"complexity {complexity}");
            if (nesting    > MaxNesting)    reasons.Add($"nesting depth {nesting}");

            yield return new RawIssue
            {
                RuleId     = RuleId,
                FilePath   = filePath,
                StartLine  = lineSpan.StartLinePosition.Line + 1,
                EndLine    = lineSpan.EndLinePosition.Line   + 1,
                Severity   = lines > MaxLines * 2 ? Severity.High : Severity.Medium,
                Message    = $"Method '{name}' is too complex ({string.Join(", ", reasons)}).",
                Category   = "Maintainability",
                Snippet    = method.ToString().Split('\n').First().Trim(),
                Suggestion = $"Extract sub-responsibilities from '{name}' into smaller private methods.",
                Scope      = new LogicalScope
                {
                    Namespace  = ns?.Name.ToString(),
                    ClassName  = cls?.Identifier.Text,
                    MemberName = name,
                    Kind       = ScopeKind.Method
                }
            };
        }
    }

    private static int ComputeComplexity(MethodDeclarationSyntax method) =>
        method.DescendantNodes().Count(n => n is
            IfStatementSyntax or
            ForStatementSyntax or
            ForEachStatementSyntax or
            WhileStatementSyntax or
            CatchClauseSyntax or
            SwitchSectionSyntax or
            ConditionalExpressionSyntax) + 1;

    private static int ComputeMaxNesting(MethodDeclarationSyntax method)
    {
        int max = 0;
        void Walk(SyntaxNode node, int depth)
        {
            if (depth > max) max = depth;
            foreach (var child in node.ChildNodes())
            {
                int next = child is IfStatementSyntax or ForStatementSyntax or
                           ForEachStatementSyntax or WhileStatementSyntax or
                           DoStatementSyntax or TryStatementSyntax
                    ? depth + 1
                    : depth;
                Walk(child, next);
            }
        }
        Walk(method, 0);
        return max;
    }
}

/// <summary>Alias used by CSharpStructuralPlugin and tests.</summary>
public sealed class ComplexMethodRule : MethodExtractionRule { }
