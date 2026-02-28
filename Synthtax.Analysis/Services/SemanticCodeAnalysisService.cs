using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Services;

/// <summary>
/// Semantic (data-flow aware) code analysis service.
/// Returns analysis results directly. Caching/persistence is handled by the caller (e.g. the API).
/// </summary>
public class SemanticCodeAnalysisService
{
    private readonly ILogger<SemanticCodeAnalysisService> _logger;
    private const int CognitiveComplexityThreshold = 15;

    public SemanticCodeAnalysisService(ILogger<SemanticCodeAnalysisService> logger)
    {
        _logger = logger;
    }

    public async Task<SemanticAnalysisResultDto> FindDeadVariablesSemanticAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "SemanticDeadVariable"
        };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag       = new ConcurrentBag<SavedIssueDto>();
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken      = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var root   = await doc.GetSyntaxRootAsync(ct);
                        var model  = await doc.GetSemanticModelAsync(ct);
                        if (root is null || model is null) return;
                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        var filePath   = doc.FilePath ?? doc.Name;
                        var fileName   = doc.Name;
                        foreach (var issue in FindDeadVariablesInDocument(root, model, sourceText, filePath, fileName))
                            bag.Add(issue);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    { _logger.LogWarning(ex, "Dead variable analysis failed for {Doc}", doc.Name); }
                });

                result.Issues = bag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList();
            }
            result.TotalIssues = result.Issues.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic dead variable analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }
        return result;
    }

    public async Task<SemanticAnalysisResultDto> FindHighCognitiveComplexityAsync(
        string solutionPath,
        int threshold = CognitiveComplexityThreshold,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "CognitiveComplexity"
        };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag       = new ConcurrentBag<SavedIssueDto>();
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken      = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var root = await doc.GetSyntaxRootAsync(ct);
                        if (root is null) return;
                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        var filePath   = doc.FilePath ?? doc.Name;
                        var fileName   = doc.Name;

                        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            var methodName = method.Identifier.Text;
                            var complexity = CognitiveComplexityCalculator.Calculate(method, methodName);
                            if (complexity < threshold) continue;

                            var span      = method.GetLocation().GetLineSpan();
                            var startLine = span.StartLinePosition.Line + 1;
                            var endLine   = span.EndLinePosition.Line + 1;
                            var snippet   = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, endLine, contextLines: 0);
                            var (desc, fixedCode, _) = CodeFixSuggestionService.ForCognitiveComplexity(methodName, complexity, threshold, snippet);
                            var containingClass = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;

                            bag.Add(new SavedIssueDto
                            {
                                FilePath         = filePath, FileName = fileName,
                                LineNumber       = startLine, EndLineNumber = endLine,
                                IssueType        = "CognitiveComplexity",
                                Severity         = CodeFixSuggestionService.MapComplexityToSeverity(complexity),
                                Description      = $"Method '{methodName}' has cognitive complexity {complexity} (threshold: {threshold}).",
                                CodeSnippet      = snippet, SuggestedFix = desc,
                                FixedCodeSnippet = fixedCode, MethodName = methodName,
                                ClassName        = containingClass, IsAutoFixable = false
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    { _logger.LogWarning(ex, "Cognitive complexity failed for {Doc}", doc.Name); }
                });

                result.Issues = bag.OrderByDescending(i =>
                {
                    var parts = i.Description.Split("complexity ");
                    return parts.Length > 1 && int.TryParse(parts[1].Split(' ')[0], out var c) ? c : 0;
                }).ToList();
            }
            result.TotalIssues = result.Issues.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cognitive complexity analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }
        return result;
    }

    public async Task<SemanticAnalysisResultDto> FindAsyncHygieneIssuesAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "AsyncHygiene"
        };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag       = new ConcurrentBag<SavedIssueDto>();
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
                        var filePath   = doc.FilePath ?? doc.Name;
                        var fileName   = doc.Name;

                        // async void check
                        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword)) continue;
                            if (!method.ReturnType.ToString().Equals("void", StringComparison.Ordinal)) continue;
                            var pars = method.ParameterList.Parameters;
                            bool isEventHandler = pars.Count == 2 &&
                                pars[0].Type?.ToString() is "object" or "object?" &&
                                (pars[1].Type?.ToString().Contains("EventArgs") ?? false);
                            if (isEventHandler) continue;

                            var span = method.GetLocation().GetLineSpan();
                            var sig  = BuildSignature(method);
                            var (desc, fixedCode, autoFix) = CodeFixSuggestionService.ForAsyncVoid(sig);
                            bag.Add(new SavedIssueDto
                            {
                                FilePath = filePath, FileName = fileName,
                                LineNumber = span.StartLinePosition.Line + 1, EndLineNumber = span.StartLinePosition.Line + 1,
                                IssueType = "AsyncVoid", Severity = Severity.Low,
                                Description = desc, CodeSnippet = sig, SuggestedFix = desc,
                                FixedCodeSnippet = fixedCode, MethodName = method.Identifier.Text,
                                ClassName = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                                IsAutoFixable = autoFix
                            });
                        }

                        // .Result / .Wait() check
                        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                        {
                            var name = memberAccess.Name.Identifier.Text;
                            if (name is not ("Result" or "Wait")) continue;
                            var typeInfo = model.GetTypeInfo(memberAccess.Expression);
                            if (!(typeInfo.Type?.Name.StartsWith("Task", StringComparison.Ordinal) ?? false)) continue;

                            var span        = memberAccess.GetLocation().GetLineSpan();
                            var startLine   = span.StartLinePosition.Line + 1;
                            var snippet     = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, startLine);
                            var containingMethod = memberAccess.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                            var (desc, fixedCode, autoFix) = CodeFixSuggestionService.ForDotResultOrWait(
                                snippet, containingMethod?.Identifier.Text ?? "unknown");

                            bag.Add(new SavedIssueDto
                            {
                                FilePath = filePath, FileName = fileName,
                                LineNumber = startLine, EndLineNumber = startLine,
                                IssueType = name == "Result" ? "TaskDotResult" : "TaskDotWait",
                                Severity = Severity.Critical,
                                Description = desc, CodeSnippet = snippet, SuggestedFix = desc,
                                FixedCodeSnippet = fixedCode,
                                MethodName = containingMethod?.Identifier.Text,
                                ClassName = containingMethod?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                                IsAutoFixable = false
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    { _logger.LogWarning(ex, "Async hygiene failed for {Doc}", doc.Name); }
                });

                result.Issues = bag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList();
            }
            result.TotalIssues = result.Issues.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Async hygiene analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }
        return result;
    }

    // ── private helpers ────────────────────────────────────────────────────────

    private static List<SavedIssueDto> FindDeadVariablesInDocument(
        SyntaxNode root, SemanticModel model,
        string sourceText, string filePath, string fileName)
    {
        var issues = new List<SavedIssueDto>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Body is null) continue;
            DataFlowAnalysis? dataFlow;
            try { dataFlow = model.AnalyzeDataFlow(method.Body); }
            catch { continue; }
            if (dataFlow is null || !dataFlow.Succeeded) continue;

            var readInside = dataFlow.ReadInside.ToHashSet();
            foreach (var declared in dataFlow.VariablesDeclared)
            {
                if (declared.Name.StartsWith("_")) continue;
                if (declared is ILocalSymbol local && IsCatchVariable(local, method.Body)) continue;
                if (declared is IParameterSymbol param && param.RefKind == RefKind.Out) continue;
                if (IsUsingDeclaration(declared, method.Body, model)) continue;
                if (readInside.Contains(declared)) continue;

                var locations = declared.Locations;
                if (locations.IsEmpty) continue;
                var loc      = locations[0];
                var span     = loc.GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var endLine   = span.EndLinePosition.Line + 1;
                var snippet  = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, endLine);
                var declarationLine = sourceText.Split('\n').ElementAtOrDefault(startLine - 1) ?? string.Empty;
                var (desc, fixedCode, autoFix) = CodeFixSuggestionService.ForDeadVariable(declared.Name, snippet, declarationLine);
                var containingClass = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text;

                issues.Add(new SavedIssueDto
                {
                    FilePath = filePath, FileName = fileName,
                    LineNumber = startLine, EndLineNumber = endLine,
                    IssueType = "DeadVariable_Semantic", Severity = Severity.Medium,
                    Description = $"Variable '{declared.Name}' is declared but never read.",
                    CodeSnippet = snippet, SuggestedFix = desc,
                    FixedCodeSnippet = fixedCode, MethodName = method.Identifier.Text,
                    ClassName = containingClass, IsAutoFixable = autoFix
                });
            }
        }
        return issues;
    }

    private static bool IsCatchVariable(ILocalSymbol local, BlockSyntax methodBody)
    {
        foreach (var catchClause in methodBody.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            if (catchClause.Declaration is null) continue;
            foreach (var l in local.Locations)
                if (catchClause.Declaration.Span.Contains(l.SourceSpan)) return true;
        }
        return false;
    }

    private static bool IsUsingDeclaration(ISymbol symbol, BlockSyntax methodBody, SemanticModel model)
    {
        foreach (var usingDecl in methodBody.DescendantNodes().OfType<UsingStatementSyntax>())
        {
            if (usingDecl.Declaration is null) continue;
            foreach (var variable in usingDecl.Declaration.Variables)
            {
                var varSymbol = model.GetDeclaredSymbol(variable);
                if (SymbolEqualityComparer.Default.Equals(varSymbol, symbol)) return true;
            }
        }
        foreach (var localDecl in methodBody.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;
            foreach (var variable in localDecl.Declaration.Variables)
            {
                var varSymbol = model.GetDeclaredSymbol(variable);
                if (SymbolEqualityComparer.Default.Equals(varSymbol, symbol)) return true;
            }
        }
        return false;
    }

    private static string BuildSignature(MethodDeclarationSyntax method)
    {
        var modifiers = string.Join(" ", method.Modifiers.Select(m => m.Text));
        return $"{modifiers} {method.ReturnType} {method.Identifier.Text}{method.ParameterList}".Trim();
    }
}
