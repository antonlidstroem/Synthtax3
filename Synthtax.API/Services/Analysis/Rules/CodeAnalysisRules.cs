using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis.Rules;

// ─────────────────────────────────────────────────────────────────────────────
// LongMethod
// ─────────────────────────────────────────────────────────────────────────────

public sealed class LongMethodRule : IAnalysisRule<CodeIssueDto>
{
    private readonly int _maxLines;

    public string RuleId => "CA001";
    public string Name => "Long Method";
    public bool IsEnabled => true;

    public LongMethodRule(int maxLines = 50) => _maxLines = maxLines;

    public IEnumerable<CodeIssueDto> Analyze(
        SyntaxNode root, SemanticModel? _, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            var span = method.GetLocation().GetLineSpan();
            var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            if (lineCount <= _maxLines) continue;

            var containingType = method.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.Text ?? "Unknown";

            yield return new CodeIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "LongMethod",
                Description = $"Method '{method.Identifier.Text}' is {lineCount} lines (limit: {_maxLines}).",
                LineNumber = span.StartLinePosition.Line + 1,
                LineCount = lineCount,
                MethodName = method.Identifier.Text,
                Snippet = $"{containingType}.{method.Identifier.Text}",
                Severity = lineCount > _maxLines * 2 ? Severity.High : Severity.Medium
            };
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DeadVariable — uses SymbolEqualityComparer to avoid false positives from
// simple text matching (e.g. two distinct 'result' variables in the same method).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class DeadVariableRule : IAnalysisRule<CodeIssueDto>
{
    public string RuleId => "CA002";
    public string Name => "Unused Variable";
    public bool IsEnabled => true;

    public IEnumerable<CodeIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        if (model is null) yield break;

        var fileName = Path.GetFileName(filePath);

        foreach (var decl in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var variable in decl.Declaration.Variables)
            {
                if (model.GetDeclaredSymbol(variable) is not ILocalSymbol symbol) continue;

                var containingMethod = decl.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();
                if (containingMethod is null) continue;

                var methodBody = (SyntaxNode?)containingMethod.Body
                                 ?? containingMethod.ExpressionBody;
                if (methodBody is null) continue;

                // ── KEY IMPROVEMENT: compare symbols, not identifier text ──────────
                // Old code compared Identifier.Text → false positives when two distinct
                // variables share the same name in nested scopes.
                bool isUsed = methodBody
                    .DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(id => id.SpanStart != variable.Identifier.SpanStart)
                    .Any(id =>
                    {
                        var sym = model.GetSymbolInfo(id).Symbol;
                        return sym is not null &&
                               SymbolEqualityComparer.Default.Equals(sym, symbol);
                    });

                if (!isUsed)
                {
                    var span = variable.GetLocation().GetLineSpan();
                    yield return new CodeIssueDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        IssueType = "DeadVariable",
                        Description = $"Variable '{variable.Identifier.Text}' is declared but never used.",
                        LineNumber = span.StartLinePosition.Line + 1,
                        MethodName = containingMethod.Identifier.Text,
                        Snippet = decl.ToString().Trim(),
                        Severity = Severity.Low
                    };
                }
            }
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// UnnecessaryUsing — builds a HashSet<namespace> once per file rather than
// looping all identifiers for each using directive (O(n) vs old O(n×m)).
// ─────────────────────────────────────────────────────────────────────────────

public sealed class UnnecessaryUsingRule : IAnalysisRule<CodeIssueDto>
{
    public string RuleId => "CA003";
    public string Name => "Unnecessary Using";
    public bool IsEnabled => true;

    public IEnumerable<CodeIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        if (model is null) yield break;

        var fileName = Path.GetFileName(filePath);

        // ── STEP 1: collect all non-alias, non-static using directives ────────
        var usingDirectives = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => u.StaticKeyword.IsKind(SyntaxKind.None) && u.Alias is null)
            .ToList();

        if (usingDirectives.Count == 0) yield break;

        // ── STEP 2: build used-namespace set in ONE pass over all identifiers ──
        // Old code: foreach(using) { foreach(identifier) { check } }  → O(u × i)
        // New code: foreach(identifier) { collect ns }  → O(i), then check O(u)
        var usedNamespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            var sym = model.GetSymbolInfo(id).Symbol
                      ?? model.GetSymbolInfo(id).CandidateSymbols.FirstOrDefault();
            if (sym is null) continue;

            var ns = sym.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrEmpty(ns))
                usedNamespaces.Add(ns);
        }

        // ── STEP 3: report usings whose namespace was never seen ──────────────
        foreach (var usingDir in usingDirectives)
        {
            if (usingDir.Name is null) continue;
            var namespaceName = usingDir.Name.ToString();

            // The using covers 'namespaceName' and all its children
            bool isUsed = usedNamespaces.Any(ns =>
                ns == namespaceName ||
                ns.StartsWith(namespaceName + ".", StringComparison.Ordinal));

            if (!isUsed)
            {
                var span = usingDir.GetLocation().GetLineSpan();
                yield return new CodeIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "UnnecessaryUsing",
                    Description = $"Using directive '{namespaceName}' appears to be unnecessary.",
                    LineNumber = span.StartLinePosition.Line + 1,
                    Snippet = usingDir.ToString().Trim(),
                    Severity = Severity.Low
                };
            }
        }
    }
}
