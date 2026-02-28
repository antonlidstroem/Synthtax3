using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Services;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Services;

public class SemanticSecurityAnalysisService
{
    private readonly ILogger<SemanticSecurityAnalysisService> _logger;

    private static readonly HashSet<string> SqlCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SqlCommand", "SqlConnection", "NpgsqlCommand", "MySqlCommand",
        "OracleCommand", "SQLiteCommand", "SqliteCommand"
    };

    private static readonly HashSet<string> SafeEfMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "FromSqlInterpolated", "ExecuteSqlInterpolated", "ExecuteSqlInterpolatedAsync"
    };

    public SemanticSecurityAnalysisService(ILogger<SemanticSecurityAnalysisService> logger)
        => _logger = logger;

    public async Task<SemanticAnalysisResultDto> FindSqlInjectionRisksSemanticAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto { SolutionPath = solutionPath, AnalysisType = "SemanticSqlInjection" };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                var documents       = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag             = new ConcurrentBag<SavedIssueDto>();
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken      = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var root  = await doc.GetSyntaxRootAsync(ct);
                        var model = await doc.GetSemanticModelAsync(ct);
                        if (root is null || model is null) return;
                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        foreach (var issue in FindSqlInjectionInDocument(root, model, sourceText, doc.FilePath ?? doc.Name, doc.Name))
                            bag.Add(issue);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { _logger.LogWarning(ex, "SQL injection analysis failed for {Doc}", doc.Name); }
                });

                result.Issues = bag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList();
            }
            result.TotalIssues = result.Issues.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic SQL injection analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }
        return result;
    }

    public async Task<SemanticAnalysisResultDto> FindMissingCancellationTokensSemanticAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto { SolutionPath = solutionPath, AnalysisType = "MissingCancellationToken" };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                var documents       = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag             = new ConcurrentBag<SavedIssueDto>();
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken      = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var root  = await doc.GetSyntaxRootAsync(ct);
                        var model = await doc.GetSemanticModelAsync(ct);
                        if (root is null || model is null) return;
                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        foreach (var issue in FindMissingCtInDocument(root, model, sourceText, doc.FilePath ?? doc.Name, doc.Name))
                            bag.Add(issue);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { _logger.LogWarning(ex, "CT analysis failed for {Doc}", doc.Name); }
                });

                result.Issues = bag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList();
            }
            result.TotalIssues = result.Issues.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CT analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }
        return result;
    }

    // ── private helpers (unchanged logic, only namespace updated) ──────────────

    private List<SavedIssueDto> FindSqlInjectionInDocument(
        SyntaxNode root, SemanticModel model, string sourceText, string filePath, string fileName)
    {
        var issues = new List<SavedIssueDto>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol) continue;
            var typeName = methodSymbol.ContainingType?.Name ?? string.Empty;
            if (SafeEfMethods.Contains(methodSymbol.Name)) continue;

            bool isRawSqlCall =
                SqlCommandTypes.Contains(typeName) ||
                ((typeName.Contains("DbContext") || typeName.Contains("DbSet")) &&
                 methodSymbol.Name is "FromSqlRaw" or "ExecuteSqlRaw" or "ExecuteSqlRawAsync");

            if (!isRawSqlCall) continue;

            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                if (!IsVulnerableStringArgument(arg.Expression, out bool isInterpolation)) continue;
                var span      = invocation.GetLocation().GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var snippet   = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, span.EndLinePosition.Line + 1);
                var containingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var (desc, fixedCode, _) = CodeFixSuggestionService.ForSqlInjection(
                    snippet, containingMethod?.Identifier.Text ?? methodSymbol.Name, isInterpolation);

                issues.Add(new SavedIssueDto
                {
                    FilePath = filePath, FileName = fileName,
                    LineNumber = startLine, EndLineNumber = span.EndLinePosition.Line + 1,
                    IssueType = "SqlInjection_Semantic", Severity = Severity.Critical,
                    Description = $"SQL injection risk: {(isInterpolation ? "interpolated" : "concatenated")} string passed to {typeName}.{methodSymbol.Name}.",
                    CodeSnippet = snippet, SuggestedFix = desc, FixedCodeSnippet = fixedCode,
                    MethodName = containingMethod?.Identifier.Text,
                    ClassName = containingMethod?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                    IsAutoFixable = false
                });
                break;
            }
        }

        foreach (var objCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (model.GetTypeInfo(objCreation).Type is not { } typeSymbol) continue;
            if (!SqlCommandTypes.Contains(typeSymbol.Name) || objCreation.ArgumentList is null) continue;

            foreach (var arg in objCreation.ArgumentList.Arguments)
            {
                if (!IsVulnerableStringArgument(arg.Expression, out bool isInterp)) continue;
                var span      = objCreation.GetLocation().GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var snippet   = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, span.EndLinePosition.Line + 1);
                var containingMethod = objCreation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var (desc, fixedCode, _) = CodeFixSuggestionService.ForSqlInjection(snippet, containingMethod?.Identifier.Text ?? "unknown", isInterp);

                issues.Add(new SavedIssueDto
                {
                    FilePath = filePath, FileName = fileName,
                    LineNumber = startLine, EndLineNumber = span.EndLinePosition.Line + 1,
                    IssueType = "SqlInjection_Semantic", Severity = Severity.Critical,
                    Description = $"SQL injection risk: {(isInterp ? "interpolated" : "concatenated")} string passed to new {typeSymbol.Name}().",
                    CodeSnippet = snippet, SuggestedFix = desc, FixedCodeSnippet = fixedCode,
                    MethodName = containingMethod?.Identifier.Text,
                    ClassName = containingMethod?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                    IsAutoFixable = false
                });
                break;
            }
        }

        return issues;
    }

    private static bool IsVulnerableStringArgument(ExpressionSyntax expr, out bool isInterpolation)
    {
        isInterpolation = false;
        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            bool hasInsertions = interpolated.Contents.OfType<InterpolationSyntax>().Any();
            isInterpolation = hasInsertions;
            return hasInsertions;
        }
        if (expr is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.AddExpression))
            return IsStringLiteralOrConcat(bin.Left) && !IsStringLiteralOrConcat(bin.Right);
        return false;
    }

    private static bool IsStringLiteralOrConcat(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression) ||
        expr is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression);

    private static List<SavedIssueDto> FindMissingCtInDocument(
        SyntaxNode root, SemanticModel model, string sourceText, string filePath, string fileName)
    {
        var issues = new List<SavedIssueDto>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword)) continue;
            var returnTypeName = method.ReturnType.ToString();
            if (!returnTypeName.StartsWith("Task", StringComparison.Ordinal) &&
                !returnTypeName.StartsWith("ValueTask", StringComparison.Ordinal)) continue;

            bool hasCt = method.ParameterList.Parameters.Any(p =>
                p.Type?.ToString().Contains("CancellationToken") ?? false);
            if (hasCt) continue;

            var symbol = model.GetDeclaredSymbol(method);
            if (symbol is null || symbol.IsOverride || symbol.ExplicitInterfaceImplementations.Any()) continue;
            if (IsImplicitInterfaceImplementation(symbol, model)) continue;

            var pars = method.ParameterList.Parameters;
            if (pars.Count == 2 && (pars[0].Type?.ToString() is "object" or "object?") &&
                (pars[1].Type?.ToString().Contains("EventArgs") ?? false)) continue;

            var innerCalls = FindInnerCallsAcceptingCt(method, model);
            if (innerCalls.Count == 0) continue;

            var span      = method.GetLocation().GetLineSpan();
            var startLine = span.StartLinePosition.Line + 1;
            var sig       = BuildSignature(method);
            var snippet   = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, startLine);
            var (desc, fixedCode, _) = CodeFixSuggestionService.ForMissingCancellationToken(sig, method.Identifier.Text, innerCalls);

            issues.Add(new SavedIssueDto
            {
                FilePath = filePath, FileName = fileName,
                LineNumber = startLine, EndLineNumber = startLine,
                IssueType = "MissingCancellationToken_Semantic", Severity = Severity.Medium,
                Description = $"Async method '{method.Identifier.Text}' does not accept CancellationToken. Inner calls that support it: {string.Join(", ", innerCalls.Take(3))}.",
                CodeSnippet = snippet, SuggestedFix = desc, FixedCodeSnippet = fixedCode,
                MethodName = method.Identifier.Text,
                ClassName = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                IsAutoFixable = false
            });
        }
        return issues;
    }

    private static bool IsImplicitInterfaceImplementation(IMethodSymbol symbol, SemanticModel model)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null) return false;
        foreach (var iface in containingType.AllInterfaces)
        foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
        {
            var impl = containingType.FindImplementationForInterfaceMember(member);
            if (SymbolEqualityComparer.Default.Equals(impl, symbol)) return true;
        }
        return false;
    }

    private static List<string> FindInnerCallsAcceptingCt(MethodDeclarationSyntax method, SemanticModel model)
    {
        var result = new List<string>();
        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol) continue;
            if (!methodSymbol.Parameters.Any(p => p.Type.Name == "CancellationToken")) continue;
            if (invocation.ArgumentList.Arguments.Any(a =>
                model.GetTypeInfo(a.Expression).Type?.Name == "CancellationToken")) continue;
            result.Add($"{methodSymbol.ContainingType?.Name}.{methodSymbol.Name}(...)");
        }
        return result.Distinct().ToList();
    }

    private static string BuildSignature(MethodDeclarationSyntax method)
    {
        var modifiers = string.Join(" ", method.Modifiers.Select(m => m.Text));
        return $"{modifiers} {method.ReturnType} {method.Identifier.Text}{method.ParameterList}".Trim();
    }
}
