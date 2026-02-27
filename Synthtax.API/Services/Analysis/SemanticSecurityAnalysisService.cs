using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// Semantic security analysis using Roslyn symbol resolution.
/// Works alongside the existing syntactic SecurityAnalysisService.
/// </summary>
public class SemanticSecurityAnalysisService
{
    private readonly ILogger<SemanticSecurityAnalysisService> _logger;
    private readonly IAnalysisCacheService _cache;

    // ADO.NET and common DB provider command types
    private static readonly HashSet<string> SqlCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SqlCommand", "SqlConnection",
        "NpgsqlCommand",             // PostgreSQL
        "MySqlCommand",              // MySQL
        "OracleCommand",             // Oracle
        "SQLiteCommand",             // SQLite ADO
        "SqliteCommand"
    };

    // EF Core methods that are safe (use real parameterization)
    private static readonly HashSet<string> SafeEfMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "FromSqlInterpolated",
        "ExecuteSqlInterpolated",
        "ExecuteSqlInterpolatedAsync"
    };

    public SemanticSecurityAnalysisService(
        ILogger<SemanticSecurityAnalysisService> logger,
        IAnalysisCacheService cache)
    {
        _logger = logger;
        _cache = cache;
    }

    // ── SQL Injection (Symbol-level) ───────────────────────────────────────────

    public async Task<SemanticAnalysisResultDto> FindSqlInjectionRisksSemanticAsync(
        string solutionPath,
        bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "SemanticSqlInjection"
        };

        Guid? sessionId = null;
        if (saveToCache)
            sessionId = await _cache.CreateSessionAsync(
                solutionPath, "SemanticSqlInjection", cancellationToken: cancellationToken);

        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);

            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag = new ConcurrentBag<SavedIssueDto>();

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var root = await doc.GetSyntaxRootAsync(ct);
                        var model = await doc.GetSemanticModelAsync(ct);
                        if (root is null || model is null) return;

                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        var filePath = doc.FilePath ?? doc.Name;
                        var fileName = doc.Name;

                        var issues = FindSqlInjectionInDocument(
                            root, model, sourceText, filePath, fileName);
                        foreach (var issue in issues)
                            bag.Add(issue);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SQL injection analysis failed for {Doc}", doc.Name);
                    }
                });

                result.Issues = bag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList();
            }

            result.TotalIssues = result.Issues.Count;

            if (saveToCache && sessionId.HasValue && result.Issues.Count > 0)
                await _cache.SaveIssuesAsync(sessionId.Value, result.Issues, cancellationToken);

            if (sessionId.HasValue)
                result.SessionId = sessionId.Value;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic SQL injection analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    private List<SavedIssueDto> FindSqlInjectionInDocument(
        SyntaxNode root,
        SemanticModel model,
        string sourceText,
        string filePath,
        string fileName)
    {
        var issues = new List<SavedIssueDto>();

        // Strategy 1: Constructor / method calls on SQL command types
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

            var typeName = methodSymbol.ContainingType?.Name ?? string.Empty;

            // Skip explicitly safe EF Core methods
            if (SafeEfMethods.Contains(methodSymbol.Name)) continue;

            bool isRawSqlCall =
                SqlCommandTypes.Contains(typeName) ||
                (typeName.Contains("DbContext") || typeName.Contains("DbSet")) &&
                (methodSymbol.Name is "FromSqlRaw" or "ExecuteSqlRaw" or "ExecuteSqlRawAsync");

            if (!isRawSqlCall) continue;

            // Check if any argument is a string concatenation or interpolation
            foreach (var arg in invocation.ArgumentList.Arguments)
            {
                var argExpr = arg.Expression;
                bool isVulnerable = IsVulnerableStringArgument(argExpr, out bool isInterpolation);
                if (!isVulnerable) continue;

                var span = invocation.GetLocation().GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var endLine = span.EndLinePosition.Line + 1;
                var snippet = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, endLine);

                var containingMethod = invocation.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();

                var (desc, fixedCode, _) = CodeFixSuggestionService.ForSqlInjection(
                    snippet, containingMethod?.Identifier.Text ?? methodSymbol.Name, isInterpolation);

                issues.Add(new SavedIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    LineNumber = startLine,
                    EndLineNumber = endLine,
                    IssueType = "SqlInjection_Semantic",
                    Severity = Severity.Critical,
                    Description = $"SQL injection risk: {(isInterpolation ? "interpolated" : "concatenated")} string passed to {typeName}.{methodSymbol.Name}.",
                    CodeSnippet = snippet,
                    SuggestedFix = desc,
                    FixedCodeSnippet = fixedCode,
                    MethodName = containingMethod?.Identifier.Text,
                    ClassName = containingMethod?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                    IsAutoFixable = false
                });
                break; // one issue per invocation
            }
        }

        // Strategy 2: ObjectCreationExpression for new SqlCommand(concatenation)
        foreach (var objCreation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var typeSymbol = model.GetTypeInfo(objCreation).Type;
            if (typeSymbol is null) continue;
            if (!SqlCommandTypes.Contains(typeSymbol.Name)) continue;

            if (objCreation.ArgumentList is null) continue;
            foreach (var arg in objCreation.ArgumentList.Arguments)
            {
                bool isVulnerable = IsVulnerableStringArgument(arg.Expression, out bool isInterp);
                if (!isVulnerable) continue;

                var span = objCreation.GetLocation().GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var endLine = span.EndLinePosition.Line + 1;
                var snippet = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, endLine);
                var containingMethod = objCreation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                var (desc, fixedCode, _) = CodeFixSuggestionService.ForSqlInjection(
                    snippet, containingMethod?.Identifier.Text ?? "unknown", isInterp);

                issues.Add(new SavedIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    LineNumber = startLine,
                    EndLineNumber = endLine,
                    IssueType = "SqlInjection_Semantic",
                    Severity = Severity.Critical,
                    Description = $"SQL injection risk: {(isInterp ? "interpolated" : "concatenated")} string passed to new {typeSymbol.Name}().",
                    CodeSnippet = snippet,
                    SuggestedFix = desc,
                    FixedCodeSnippet = fixedCode,
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

        // Direct interpolated string: $"SELECT ... {userId}"
        if (expr is InterpolatedStringExpressionSyntax interpolated)
        {
            // Only vulnerable if it contains actual interpolations (not just constant text)
            bool hasInsertions = interpolated.Contents.OfType<InterpolationSyntax>().Any();
            isInterpolation = hasInsertions;
            return hasInsertions;
        }

        // String concatenation: "SELECT * WHERE id = " + userId
        if (expr is BinaryExpressionSyntax bin &&
            bin.IsKind(SyntaxKind.AddExpression))
        {
            // At least one side should be a string literal for it to be SQL
            bool leftIsString = IsStringLiteralOrConcat(bin.Left);
            bool rightIsNonLiteral = !IsStringLiteralOrConcat(bin.Right);
            return leftIsString && rightIsNonLiteral;
        }

        return false;
    }

    private static bool IsStringLiteralOrConcat(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit &&
        lit.IsKind(SyntaxKind.StringLiteralExpression) ||
        expr is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression);

    // ── Missing CancellationToken (Contract-aware) ─────────────────────────────

    public async Task<SemanticAnalysisResultDto> FindMissingCancellationTokensSemanticAsync(
        string solutionPath,
        bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "MissingCancellationToken"
        };

        Guid? sessionId = null;
        if (saveToCache)
            sessionId = await _cache.CreateSessionAsync(
                solutionPath, "MissingCancellationToken", cancellationToken: cancellationToken);

        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);

            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag = new ConcurrentBag<SavedIssueDto>();

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var root = await doc.GetSyntaxRootAsync(ct);
                        var model = await doc.GetSemanticModelAsync(ct);
                        if (root is null || model is null) return;

                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        var filePath = doc.FilePath ?? doc.Name;
                        var fileName = doc.Name;

                        var issues = FindMissingCtInDocument(
                            root, model, sourceText, filePath, fileName);
                        foreach (var issue in issues)
                            bag.Add(issue);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "CancellationToken analysis failed for {Doc}", doc.Name);
                    }
                });

                result.Issues = bag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList();
            }

            result.TotalIssues = result.Issues.Count;

            if (saveToCache && sessionId.HasValue && result.Issues.Count > 0)
                await _cache.SaveIssuesAsync(sessionId.Value, result.Issues, cancellationToken);

            if (sessionId.HasValue)
                result.SessionId = sessionId.Value;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CT analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    private static List<SavedIssueDto> FindMissingCtInDocument(
        SyntaxNode root,
        SemanticModel model,
        string sourceText,
        string filePath,
        string fileName)
    {
        var issues = new List<SavedIssueDto>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            // Must be async Task / Task<T>
            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword)) continue;
            var returnTypeName = method.ReturnType.ToString();
            if (!returnTypeName.StartsWith("Task", StringComparison.Ordinal) &&
                !returnTypeName.StartsWith("ValueTask", StringComparison.Ordinal)) continue;

            // Already has CancellationToken parameter
            bool hasCt = method.ParameterList.Parameters.Any(p =>
                p.Type?.ToString().Contains("CancellationToken") ?? false);
            if (hasCt) continue;

            // Contract-safety: skip overrides and interface implementations
            var symbol = model.GetDeclaredSymbol(method);
            if (symbol is null) continue;

            if (symbol.IsOverride) continue;
            if (symbol.ExplicitInterfaceImplementations.Any()) continue;

            // Check if containing type implements an interface that declares this method
            // (implicit interface implementation)
            if (IsImplicitInterfaceImplementation(symbol)) continue;

            // Skip event handlers: (object sender, XxxEventArgs e)
            var parameters = method.ParameterList.Parameters;
            if (parameters.Count == 2 &&
                (parameters[0].Type?.ToString() is "object" or "object?") &&
                (parameters[1].Type?.ToString().Contains("EventArgs") ?? false))
                continue;

            // Must contain at least one awaited call that accepts CancellationToken
            var innerCalls = FindInnerCallsAcceptingCt(method, model);
            if (innerCalls.Count == 0) continue;

            var span = method.GetLocation().GetLineSpan();
            var startLine = span.StartLinePosition.Line + 1;
            var sig = BuildSignature(method);
            var snippet = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, startLine);

            var (desc, fixedCode, _) = CodeFixSuggestionService
                .ForMissingCancellationToken(sig, method.Identifier.Text, innerCalls);

            var containingClass = method.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.Text;

            issues.Add(new SavedIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                LineNumber = startLine,
                EndLineNumber = startLine,
                IssueType = "MissingCancellationToken_Semantic",
                Severity = Severity.Medium,
                Description = $"Async method '{method.Identifier.Text}' does not accept CancellationToken. Inner calls that support it: {string.Join(", ", innerCalls.Take(3))}.",
                CodeSnippet = snippet,
                SuggestedFix = desc,
                FixedCodeSnippet = fixedCode,
                MethodName = method.Identifier.Text,
                ClassName = containingClass,
                IsAutoFixable = false
            });
        }

        return issues;
    }

    private static bool IsImplicitInterfaceImplementation(IMethodSymbol symbol)
    {
        var containingType = symbol.ContainingType;
        if (containingType is null) return false;

        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var impl = containingType.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                    return true;
            }
        }
        return false;
    }

    private static List<string> FindInnerCallsAcceptingCt(
        MethodDeclarationSyntax method, SemanticModel model)
    {
        var result = new List<string>();

        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var symbolInfo = model.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol) continue;

            bool acceptsCt = methodSymbol.Parameters.Any(p =>
                p.Type.Name == "CancellationToken");
            if (!acceptsCt) continue;

            // Check if caller is NOT already passing a token
            bool alreadyPassingCt = invocation.ArgumentList.Arguments.Any(a =>
                model.GetTypeInfo(a.Expression).Type?.Name == "CancellationToken");
            if (alreadyPassingCt) continue;

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
