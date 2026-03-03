using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

public sealed class NotImplementedExceptionRule : ISynthtaxRule
{
    // Static so plugins can access RuleId without an instance
    public static string RuleId => "SA001";
    string ISynthtaxRule.RuleId => RuleId;

    // ── Interface implementation ──────────────────────────────────────────
    public IEnumerable<RawIssue> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct)
        => AnalyzeCore(root, filePath, ct);

    // ── Overload used by tests: (SyntaxTree, string, enabledRules?) ───────
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
        => new NotImplementedExceptionRule().AnalyzeCore(root, filePath, ct);

    // ── Core logic ────────────────────────────────────────────────────────
    private IEnumerable<RawIssue> AnalyzeCore(
        SyntaxNode root, string filePath, CancellationToken ct)
    {
        var throws = root.DescendantNodes()
            .Where(n => n is ThrowStatementSyntax or ThrowExpressionSyntax);

        foreach (var node in throws)
        {
            ct.ThrowIfCancellationRequested();

            ExpressionSyntax? expr = node is ThrowStatementSyntax s
                ? s.Expression
                : ((ThrowExpressionSyntax)node).Expression;

            if (expr is not ObjectCreationExpressionSyntax obj) continue;
            if (!obj.Type.ToString().EndsWith("NotImplementedException")) continue;

            var lineSpan = node.GetLocation().GetLineSpan();

            // Determine return type for starter-code hint
            var method   = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            var retType  = method?.ReturnType.ToString() ?? "void";
            var isAsync  = method?.Modifiers.Any(m => m.Text == "async") ?? false;
            var snippet  = node.ToString().Trim();
            var fixed_snippet = BuildStarterCode(method, retType, isAsync);

            yield return new RawIssue
            {
                RuleId        = RuleId,
                FilePath      = filePath,
                StartLine     = lineSpan.StartLinePosition.Line + 1,
                EndLine       = lineSpan.EndLinePosition.Line   + 1,
                Severity      = Severity.High,
                Message       = "Systemet innehåller oimplementerad kod (NotImplementedException).",
                Category      = "Completeness",
                Snippet       = snippet,
                Scope         = ExtractScope(node),
                IsAutoFixable = true,
                FixedSnippet  = fixed_snippet,
                Suggestion    = $"Implement the method body for '{method?.Identifier.Text ?? "unknown"}'."
            };
        }
    }

    private static string BuildStarterCode(MethodDeclarationSyntax? method, string retType, bool isAsync)
    {
        if (method is null) return "// TODO: implement";
        var name = method.Identifier.Text;

        if (retType == "void")
            return $"public void {name}()\n{{\n    // TODO: implement {name}\n}}";

        if (retType.Contains("Task") || isAsync)
            return $"public async Task {name}()\n{{\n    // TODO: implement {name}\n    await Task.CompletedTask;\n}}";

        if (retType == "bool")
            return $"public bool {name}()\n{{\n    // TODO: implement {name}\n    return false;\n}}";

        return $"public {retType} {name}()\n{{\n    // TODO: implement {name}\n    throw new NotImplementedException();\n}}";
    }

    private static LogicalScope ExtractScope(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var cls    = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        var ns     = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

        return new LogicalScope
        {
            Namespace  = ns?.Name.ToString(),
            ClassName  = cls?.Identifier.Text,
            MemberName = method?.Identifier.Text,
            Kind       = method is not null ? ScopeKind.Method : ScopeKind.Class
        };
    }
}
