using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

public sealed class SecurityRule : ISynthtaxRule
{
    public static string RuleId => "SEC001";
    string ISynthtaxRule.RuleId => RuleId;

    private static readonly HashSet<string> SecretKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "password", "secret", "apikey", "token" };

    public IEnumerable<RawIssue> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct)
    {
        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;

            if (lit.Parent is not EqualsValueClauseSyntax evc) continue;
            if (evc.Parent is not VariableDeclaratorSyntax vd) continue;
            if (!SecretKeywords.Any(k => vd.Identifier.Text.Contains(k))) continue;

            var lineSpan = lit.GetLocation().GetLineSpan();
            var cls      = lit.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var ns       = lit.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

            yield return new RawIssue
            {
                RuleId    = RuleId,
                FilePath  = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine   = lineSpan.EndLinePosition.Line   + 1,
                Severity  = Severity.High,
                Message   = "Potentiell hårdkodad hemlighet detekterad.",
                Category  = "Security",
                Snippet   = lit.Parent?.Parent?.ToString().Trim() ?? lit.ToString(),
                Suggestion = "Store secrets in environment variables or a secrets manager, not in source code.",
                Scope     = new LogicalScope
                {
                    Namespace  = ns?.Name.ToString(),
                    ClassName  = cls?.Identifier.Text,
                    MemberName = null,
                    Kind       = ScopeKind.Class
                }
            };
        }
    }
}
