using System.Collections.Concurrent;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class RefactoringService : IRefactoringService
{
    private readonly ILogger<RefactoringService> _logger;
    private readonly IRoslynWorkspaceService _workspace;

    public RefactoringService(ILogger<RefactoringService> logger, IRoslynWorkspaceService workspace)
    {
        _logger    = logger;
        _workspace = workspace;
    }

    public async Task<RefactoringResultDto> SuggestRefactoringsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (ws, sol) = await _workspace.LoadSolutionAsync(solutionPath, cancellationToken);
        var result    = new RefactoringResultDto { SolutionPath = solutionPath };
        var bag       = new ConcurrentBag<RefactoringSuggestionDto>();

        await using var ctx = await AnalysisContext.BuildAsync(sol, ws, _workspace, null, _logger, cancellationToken);
        await Parallel.ForEachAsync(ctx.Documents,
            new ParallelOptions { CancellationToken = cancellationToken },
            (doc, token) =>
            {
                var root  = ctx.GetRoot(doc);
                var model = ctx.GetModel(doc);
                if (root is null) return ValueTask.CompletedTask;
                foreach (var s in AnalyzeFile(root, model, ctx.GetFilePath(doc), token))
                    bag.Add(s);
                return ValueTask.CompletedTask;
            });

        result.Suggestions.AddRange(bag
            .OrderByDescending(s => s.Impact)
            .ThenBy(s => s.FilePath)
            .ThenBy(s => s.StartLine));
        return result;
    }

    public async Task<RefactoringResultDto> SuggestRefactoringsForCodeAsync(
        string code, string fileName = "input.cs", CancellationToken cancellationToken = default)
    {
        var tree   = CSharpSyntaxTree.ParseText(code, path: fileName, cancellationToken: cancellationToken);
        var root   = await tree.GetRootAsync(cancellationToken);
        var result = new RefactoringResultDto { SolutionPath = fileName };
        result.Suggestions.AddRange(AnalyzeFile(root, null, fileName, cancellationToken));
        return result;
    }

    private static IEnumerable<RefactoringSuggestionDto> AnalyzeFile(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct)
    {
        foreach (var s in SuggestExtractMethod(root, filePath, ct))           yield return s;
        foreach (var s in SuggestGuardClauses(root, filePath, ct))            yield return s;
        foreach (var s in SuggestNullCoalescing(root, model, filePath, ct))   yield return s;
        foreach (var s in SuggestPatternMatching(root, model, filePath, ct))  yield return s;
        foreach (var s in SuggestSimplifyBoolean(root, filePath, ct))         yield return s;
    }

    private static IEnumerable<RefactoringSuggestionDto> SuggestExtractMethod(
        SyntaxNode root, string filePath, CancellationToken ct)
    {
        const int threshold = 30;
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            var span = method.GetLocation().GetLineSpan();
            var loc  = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            if (loc <= threshold || method.Body is null) continue;
            var stmts = method.Body.Statements;
            if (stmts.Count < 4) continue;
            int midStart = stmts.Count / 3;
            int midEnd   = midStart * 2;
            var toExtract = stmts.Skip(midStart).Take(midEnd - midStart).ToList();
            if (toExtract.Count == 0) continue;

            yield return new RefactoringSuggestionDto
            {
                RefactoringType = "ExtractMethod",
                Title           = $"Extract block from '{method.Identifier.Text}'",
                Description     = $"Method is {loc} lines. Extracting a logical block reduces it and improves testability.",
                FilePath        = filePath, FileName = Path.GetFileName(filePath),
                StartLine       = span.StartLinePosition.Line + 1,
                EndLine         = span.EndLinePosition.Line + 1,
                OriginalCode    = method.Identifier.Text + method.ParameterList.ToString(),
                SuggestedCode   = BuildExtractedMethod(method, toExtract, $"Extract_{method.Identifier.Text}_Block"),
                Rationale       = "Long methods are hard to test and understand. Single-responsibility improves maintainability.",
                Impact          = loc > 100 ? RefactoringImpact.High : RefactoringImpact.Medium,
                EstimatedComplexityReduction = loc / 3
            };
        }
    }

    private static string BuildExtractedMethod(
        MethodDeclarationSyntax parent, List<StatementSyntax> stmts, string newName)
    {
        var modifiers = string.Join(" ", parent.Modifiers.Select(m => m.Text));
        var sb = new StringBuilder();
        sb.AppendLine($"{modifiers} void {newName}()");
        sb.AppendLine("{");
        foreach (var s in stmts) sb.AppendLine("    " + s.ToString().Trim());
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static IEnumerable<RefactoringSuggestionDto> SuggestGuardClauses(
        SyntaxNode root, string filePath, CancellationToken ct)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (method.Body is null) continue;
            var topStmts = method.Body.Statements;
            if (topStmts.Count == 0 || topStmts.First() is not IfStatementSyntax topIf) continue;
            if (topIf.Else is not null) continue;
            var ifLines = topIf.GetLocation().GetLineSpan().EndLinePosition.Line
                        - topIf.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (ifLines < 5) continue;

            var returnType = method.ReturnType.ToString();
            var earlyReturn = returnType is "void" or "Task" ? "return;" : "return default;";
            var span = method.GetLocation().GetLineSpan();

            yield return new RefactoringSuggestionDto
            {
                RefactoringType = "GuardClause",
                Title           = $"Introduce guard clause in '{method.Identifier.Text}'",
                Description     = "Replace top-level if with inverted guard clause to reduce nesting.",
                FilePath        = filePath, FileName = Path.GetFileName(filePath),
                StartLine       = span.StartLinePosition.Line + 1,
                EndLine         = topIf.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
                OriginalCode    = topIf.ToString().Trim()[..Math.Min(120, topIf.ToString().Trim().Length)],
                SuggestedCode   = $"if ({InvertCondition(topIf.Condition)})\n    {earlyReturn}\n\n// ... rest of method at top level",
                Rationale       = "Guard clauses reduce nesting depth and cognitive complexity.",
                Impact          = RefactoringImpact.Medium,
                EstimatedComplexityReduction = ifLines / 2
            };
        }
    }

    private static string InvertCondition(ExpressionSyntax cond)
    {
        if (cond is PrefixUnaryExpressionSyntax pue && pue.IsKind(SyntaxKind.LogicalNotExpression))
            return pue.Operand.ToString();
        return $"!({cond})";
    }

    private static IEnumerable<RefactoringSuggestionDto> SuggestNullCoalescing(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct)
    {
        foreach (var ternary in root.DescendantNodes().OfType<ConditionalExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!TryMatchNullCoalescing(ternary, out var subject, out var fallback)) continue;
            var span = ternary.GetLocation().GetLineSpan();
            yield return new RefactoringSuggestionDto
            {
                RefactoringType = "NullCoalescing", Title = "Use null-coalescing operator ??",
                Description = "Ternary null-check can be simplified.",
                FilePath = filePath, FileName = Path.GetFileName(filePath),
                StartLine = span.StartLinePosition.Line + 1, EndLine = span.EndLinePosition.Line + 1,
                OriginalCode = ternary.ToString().Trim(), SuggestedCode = $"{subject} ?? {fallback}",
                Rationale = "Null-coalescing is more readable and idiomatic in C#.", Impact = RefactoringImpact.Low
            };
        }
    }

    private static bool TryMatchNullCoalescing(
        ConditionalExpressionSyntax ternary, out string subject, out string fallback)
    {
        subject = fallback = "";
        if (ternary.Condition is BinaryExpressionSyntax bin &&
            bin.IsKind(SyntaxKind.NotEqualsExpression) &&
            (bin.Right is LiteralExpressionSyntax litR && litR.IsKind(SyntaxKind.NullLiteralExpression) ||
             bin.Left  is LiteralExpressionSyntax litL && litL.IsKind(SyntaxKind.NullLiteralExpression)))
        {
            subject  = bin.Right is LiteralExpressionSyntax ? bin.Left.ToString() : bin.Right.ToString();
            fallback = ternary.WhenFalse.ToString();
            if (ternary.WhenTrue.ToString() == subject) return true;
        }
        return false;
    }

    private static IEnumerable<RefactoringSuggestionDto> SuggestPatternMatching(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct)
    {
        foreach (var ifStmt in root.DescendantNodes().OfType<IfStatementSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (ifStmt.Condition is not BinaryExpressionSyntax isExpr ||
                !isExpr.IsKind(SyntaxKind.IsExpression)) continue;
            var castTarget = isExpr.Left.ToString();
            var castType   = isExpr.Right.ToString();
            var hasCast    = ifStmt.Statement.DescendantNodes().OfType<CastExpressionSyntax>()
                .Any(c => c.Type.ToString() == castType && c.Expression.ToString() == castTarget);
            if (!hasCast) continue;
            var span = ifStmt.GetLocation().GetLineSpan();
            yield return new RefactoringSuggestionDto
            {
                RefactoringType = "PatternMatching",
                Title           = $"Use pattern matching: is {castType} {char.ToLower(castType[0])}{castType[1..]}",
                Description     = "Replace is + cast with C# pattern matching variable declaration.",
                FilePath        = filePath, FileName = Path.GetFileName(filePath),
                StartLine       = span.StartLinePosition.Line + 1, EndLine = span.EndLinePosition.Line + 1,
                OriginalCode    = ifStmt.Condition.ToString(),
                SuggestedCode   = $"if ({castTarget} is {castType} {char.ToLower(castType[0])}{castType[1..]})",
                Rationale       = "Pattern matching eliminates the explicit cast and is safer.", Impact = RefactoringImpact.Low
            };
        }
    }

    private static IEnumerable<RefactoringSuggestionDto> SuggestSimplifyBoolean(
        SyntaxNode root, string filePath, CancellationToken ct)
    {
        foreach (var ifStmt in root.DescendantNodes().OfType<IfStatementSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (ifStmt.Else is null) continue;
            if (!IsReturnBool(ifStmt.Statement, true) || !IsReturnBool(ifStmt.Else.Statement, false)) continue;
            var span = ifStmt.GetLocation().GetLineSpan();
            yield return new RefactoringSuggestionDto
            {
                RefactoringType = "SimplifyBoolean", Title = "Simplify boolean return",
                Description     = "if/else returning true/false can be replaced by returning the condition directly.",
                FilePath        = filePath, FileName = Path.GetFileName(filePath),
                StartLine       = span.StartLinePosition.Line + 1, EndLine = span.EndLinePosition.Line + 1,
                OriginalCode    = ifStmt.ToString().Trim()[..Math.Min(120, ifStmt.ToString().Trim().Length)],
                SuggestedCode   = $"return {ifStmt.Condition};",
                Rationale       = "Eliminates redundant boolean literals.", Impact = RefactoringImpact.Low
            };
        }
    }

    private static bool IsReturnBool(StatementSyntax stmt, bool expectedValue)
    {
        var ret = stmt is BlockSyntax block
            ? block.Statements.FirstOrDefault() as ReturnStatementSyntax
            : stmt as ReturnStatementSyntax;
        if (ret?.Expression is not LiteralExpressionSyntax lit) return false;
        return expectedValue
            ? lit.IsKind(SyntaxKind.TrueLiteralExpression)
            : lit.IsKind(SyntaxKind.FalseLiteralExpression);
    }
}
