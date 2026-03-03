using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

public class TypeExtractionRule : ISynthtaxRule
{
    public static string RuleId => "SA002";
    string ISynthtaxRule.RuleId => RuleId;

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
        => new TypeExtractionRule().AnalyzeCore(root, filePath, ct);

    // ── Core logic ────────────────────────────────────────────────────────
    private IEnumerable<RawIssue> AnalyzeCore(
        SyntaxNode root, string filePath, CancellationToken ct)
    {
        var types = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .Where(t => t.Parent is
                NamespaceDeclarationSyntax or
                FileScopedNamespaceDeclarationSyntax or
                CompilationUnitSyntax)
            .ToList();

        if (types.Count <= 1) yield break;

        var fileBaseName = Path.GetFileNameWithoutExtension(filePath);

        // Group by name to handle partial classes
        var grouped = types.GroupBy(t => t.Identifier.Text).ToList();
        if (grouped.Count <= 1) yield break;

        // The first group is the "primary" type — flag extras
        foreach (var group in grouped.Skip(1))
        {
            ct.ThrowIfCancellationRequested();
            var type = group.First();

            // Allow small DTOs / records to cohabit
            if (type.Identifier.Text == fileBaseName) continue;
            if (type.Identifier.Text.EndsWith("Dto", StringComparison.OrdinalIgnoreCase)) continue;
            if (type.ToString().Split('\n').Length <= 12) continue;

            var lineSpan = type.GetLocation().GetLineSpan();
            var ns = type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();

            yield return new RawIssue
            {
                RuleId     = RuleId,
                FilePath   = filePath,
                StartLine  = lineSpan.StartLinePosition.Line + 1,
                EndLine    = lineSpan.EndLinePosition.Line   + 1,
                Severity   = Severity.Medium,
                Message    = $"Typen '{type.Identifier.Text}' bör flyttas till en egen fil (Multiple Types per File).",
                Category   = "Architecture",
                Snippet    = type.ToString().Split('\n').First().Trim(),
                Suggestion = $"Move '{type.Identifier.Text}' to {type.Identifier.Text}.cs",
                Scope      = new LogicalScope
                {
                    Namespace  = ns?.Name.ToString(),
                    ClassName  = type.Identifier.Text,
                    MemberName = null,
                    Kind       = ScopeKind.Class
                }
            };
        }
    }
}

/// <summary>Alias used by CSharpStructuralPlugin and tests.</summary>
public sealed class MultiClassFileRule : TypeExtractionRule { }
