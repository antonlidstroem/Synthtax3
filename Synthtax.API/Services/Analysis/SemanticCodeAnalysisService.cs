using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// Semantic analysis service using Roslyn DataFlow and SymbolInfo APIs.
/// All methods are additive – no existing service is modified.
/// </summary>
public class SemanticCodeAnalysisService
{
    private readonly ILogger<SemanticCodeAnalysisService> _logger;
    private readonly IAnalysisCacheService _cache;

    // Cognitive complexity threshold for reporting
    private const int CognitiveComplexityThreshold = 15;

    public SemanticCodeAnalysisService(
        ILogger<SemanticCodeAnalysisService> logger,
        IAnalysisCacheService cache)
    {
        _logger = logger;
        _cache = cache;
    }

    // ── Dead Variable (DataFlow-based) ─────────────────────────────────────────

    public async Task<SemanticAnalysisResultDto> FindDeadVariablesSemanticAsync(
        string solutionPath,
        bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "SemanticDeadVariable"
        };

        Guid? sessionId = null;
        if (saveToCache)
            sessionId = await _cache.CreateSessionAsync(
                solutionPath, "SemanticDeadVariable", cancellationToken: cancellationToken);

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
                        // SemanticModel created inside task – never shared between threads
                        var root = await doc.GetSyntaxRootAsync(ct);
                        var model = await doc.GetSemanticModelAsync(ct);
                        if (root is null || model is null) return;

                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        var filePath = doc.FilePath ?? doc.Name;
                        var fileName = doc.Name;

                        var issues = FindDeadVariablesInDocument(root, model, sourceText, filePath, fileName);
                        foreach (var issue in issues)
                            bag.Add(issue);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Dead variable analysis failed for {Doc}", doc.Name);
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
            _logger.LogError(ex, "Semantic dead variable analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    private static List<SavedIssueDto> FindDeadVariablesInDocument(
        SyntaxNode root,
        SemanticModel model,
        string sourceText,
        string filePath,
        string fileName)
    {
        var issues = new List<SavedIssueDto>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Body is null) continue; // expression-bodied methods skipped

            DataFlowAnalysis? dataFlow;
            try
            {
                dataFlow = model.AnalyzeDataFlow(method.Body);
            }
            catch
            {
                continue;
            }

            if (dataFlow is null || !dataFlow.Succeeded) continue;

            var readInside = dataFlow.ReadInside.ToHashSet();

            foreach (var declared in dataFlow.VariablesDeclared)
            {
                // Skip: discard (_), catch variables (by convention), out params
                if (declared.Name.StartsWith("_")) continue;
                if (declared is ILocalSymbol local && IsCatchVariable(local, method.Body)) continue;
                if (declared is IParameterSymbol param && param.RefKind == RefKind.Out) continue;

                // Skip: using declarations (IDisposable side-effects at scope exit)
                if (IsUsingDeclaration(declared, method.Body, model)) continue;

                if (readInside.Contains(declared)) continue;

                // Found a dead variable
                var locations = declared.Locations;
                if (locations.IsEmpty) continue;

                var loc = locations[0];
                var span = loc.GetLineSpan();
                var startLine = span.StartLinePosition.Line + 1;
                var endLine = span.EndLinePosition.Line + 1;

                var snippet = CodeFixSuggestionService.ExtractCodeSnippet(sourceText, startLine, endLine);
                var declarationLine = sourceText.Split('\n')
                    .ElementAtOrDefault(startLine - 1) ?? string.Empty;

                var (desc, fixedCode, autoFix) =
                    CodeFixSuggestionService.ForDeadVariable(declared.Name, snippet, declarationLine);

                var containingClass = method.Ancestors()
                    .OfType<TypeDeclarationSyntax>()
                    .FirstOrDefault()?.Identifier.Text;

                issues.Add(new SavedIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    LineNumber = startLine,
                    EndLineNumber = endLine,
                    IssueType = "DeadVariable_Semantic",
                    Severity = Severity.Warning,
                    Description = $"Variable '{declared.Name}' is declared but never read.",
                    CodeSnippet = snippet,
                    SuggestedFix = desc,
                    FixedCodeSnippet = fixedCode,
                    MethodName = method.Identifier.Text,
                    ClassName = containingClass,
                    IsAutoFixable = autoFix
                });
            }
        }

        return issues;
    }

    private static bool IsCatchVariable(ILocalSymbol local, BlockSyntax methodBody)
    {
        foreach (var catchClause in methodBody.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            if (catchClause.Declaration is not null)
            {
                var catchDecl = catchClause.Declaration;
                // Check if this variable is declared in a catch clause
                foreach (var loc in local.Locations)
                {
                    if (catchDecl.Span.Contains(loc.SourceSpan))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool IsUsingDeclaration(ISymbol symbol, BlockSyntax methodBody, SemanticModel model)
    {
        foreach (var usingDecl in methodBody.DescendantNodes().OfType<UsingStatementSyntax>())
        {
            if (usingDecl.Declaration is not null)
            {
                foreach (var variable in usingDecl.Declaration.Variables)
                {
                    var varSymbol = model.GetDeclaredSymbol(variable);
                    if (SymbolEqualityComparer.Default.Equals(varSymbol, symbol))
                        return true;
                }
            }
        }

        // Also check using declarations (C# 8 "using var")
        foreach (var localDecl in methodBody.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!localDecl.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)) continue;
            foreach (var variable in localDecl.Declaration.Variables)
            {
                var varSymbol = model.GetDeclaredSymbol(variable);
                if (SymbolEqualityComparer.Default.Equals(varSymbol, symbol))
                    return true;
            }
        }

        return false;
    }

    // ── Cognitive Complexity Analysis ──────────────────────────────────────────

    public async Task<SemanticAnalysisResultDto> FindHighCognitiveComplexityAsync(
        string solutionPath,
        int threshold = CognitiveComplexityThreshold,
        bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "CognitiveComplexity"
        };

        Guid? sessionId = null;
        if (saveToCache)
            sessionId = await _cache.CreateSessionAsync(
                solutionPath, "CognitiveComplexity", cancellationToken: cancellationToken);

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
                        if (root is null) return;

                        var sourceText = (await doc.GetTextAsync(ct)).ToString();
                        var filePath = doc.FilePath ?? doc.Name;
                        var fileName = doc.Name;

                        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            var methodName = method.Identifier.Text;
                            var complexity = CognitiveComplexityCalculator.Calculate(method, methodName);
                            if (complexity < threshold) continue;

                            var span = method.GetLocation().GetLineSpan();
                            var startLine = span.StartLinePosition.Line + 1;
                            var endLine = span.EndLinePosition.Line + 1;

                            var snippet = CodeFixSuggestionService.ExtractCodeSnippet(
                                sourceText, startLine, endLine, contextLines: 0);

                            var (desc, fixedCode, _) = CodeFixSuggestionService
                                .ForCognitiveComplexity(methodName, complexity, threshold, snippet);

                            var containingClass = method.Ancestors()
                                .OfType<TypeDeclarationSyntax>()
                                .FirstOrDefault()?.Identifier.Text;

                            bag.Add(new SavedIssueDto
                            {
                                FilePath = filePath,
                                FileName = fileName,
                                LineNumber = startLine,
                                EndLineNumber = endLine,
                                IssueType = "CognitiveComplexity",
                                Severity = CodeFixSuggestionService.MapComplexityToSeverity(complexity),
                                Description = $"Method '{methodName}' has cognitive complexity {complexity} (threshold: {threshold}).",
                                CodeSnippet = snippet,
                                SuggestedFix = desc,
                                FixedCodeSnippet = fixedCode,
                                MethodName = methodName,
                                ClassName = containingClass,
                                IsAutoFixable = false
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Cognitive complexity analysis failed for {Doc}", doc.Name);
                    }
                });

                result.Issues = bag.OrderByDescending(i =>
                {
                    // Sort by severity then file
                    if (int.TryParse(i.Description.Split("complexity ")[1].Split(" ")[0], out var c)) return c;
                    return 0;
                }).ToList();
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
            _logger.LogError(ex, "Cognitive complexity analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    // ── Async Hygiene ──────────────────────────────────────────────────────────

    public async Task<SemanticAnalysisResultDto> FindAsyncHygieneIssuesAsync(
        string solutionPath,
        bool saveToCache = true,
        CancellationToken cancellationToken = default)
    {
        var result = new SemanticAnalysisResultDto
        {
            SolutionPath = solutionPath,
            AnalysisType = "AsyncHygiene"
        };

        Guid? sessionId = null;
        if (saveToCache)
            sessionId = await _cache.CreateSessionAsync(
                solutionPath, "AsyncHygiene", cancellationToken: cancellationToken);

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

                        // 1. async void
                        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                        {
                            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword)) continue;
                            if (!method.ReturnType.ToString().Equals("void", StringComparison.Ordinal)) continue;

                            // Event handlers (sender, EventArgs) are fine as async void
                            var parameters = method.ParameterList.Parameters;
                            bool isEventHandler = parameters.Count == 2 &&
                                parameters[0].Type?.ToString() is "object" or "object?" &&
                                (parameters[1].Type?.ToString().Contains("EventArgs") ?? false);
                            if (isEventHandler) continue;

                            var span = method.GetLocation().GetLineSpan();
                            var startLine = span.StartLinePosition.Line + 1;
                            var sig = BuildSignature(method);
                            var (desc, fixedCode, autoFix) = CodeFixSuggestionService.ForAsyncVoid(sig);

                            bag.Add(new SavedIssueDto
                            {
                                FilePath = filePath,
                                FileName = fileName,
                                LineNumber = startLine,
                                EndLineNumber = startLine,
                                IssueType = "AsyncVoid",
                                Severity = Severity.Error,
                                Description = desc,
                                CodeSnippet = sig,
                                SuggestedFix = desc,
                                FixedCodeSnippet = fixedCode,
                                MethodName = method.Identifier.Text,
                                ClassName = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                                IsAutoFixable = autoFix
                            });
                        }

                        // 2. .Result / .Wait() on Tasks
                        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                        {
                            var name = memberAccess.Name.Identifier.Text;
                            if (name is not ("Result" or "Wait")) continue;

                            // Verify the expression is a Task via symbol
                            var typeInfo = model.GetTypeInfo(memberAccess.Expression);
                            var typeName = typeInfo.Type?.Name ?? string.Empty;
                            if (!typeName.StartsWith("Task", StringComparison.Ordinal)) continue;

                            var span = memberAccess.GetLocation().GetLineSpan();
                            var startLine = span.StartLinePosition.Line + 1;
                            var snippet = CodeFixSuggestionService.ExtractCodeSnippet(
                                sourceText, startLine, startLine);

                            var containingMethod = memberAccess.Ancestors()
                                .OfType<MethodDeclarationSyntax>()
                                .FirstOrDefault();

                            var (desc, fixedCode, autoFix) = CodeFixSuggestionService.ForDotResultOrWait(
                                snippet, containingMethod?.Identifier.Text ?? "unknown");

                            bag.Add(new SavedIssueDto
                            {
                                FilePath = filePath,
                                FileName = fileName,
                                LineNumber = startLine,
                                EndLineNumber = startLine,
                                IssueType = name == "Result" ? "TaskDotResult" : "TaskDotWait",
                                Severity = Severity.Critical,
                                Description = desc,
                                CodeSnippet = snippet,
                                SuggestedFix = desc,
                                FixedCodeSnippet = fixedCode,
                                MethodName = containingMethod?.Identifier.Text,
                                ClassName = containingMethod?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
                                IsAutoFixable = false
                            });
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Async hygiene analysis failed for {Doc}", doc.Name);
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
            _logger.LogError(ex, "Async hygiene analysis failed for {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    private static string BuildSignature(MethodDeclarationSyntax method)
    {
        var modifiers = string.Join(" ", method.Modifiers.Select(m => m.Text));
        return $"{modifiers} {method.ReturnType} {method.Identifier.Text}{method.ParameterList}".Trim();
    }
}

// ── Result DTO (new, additive) ─────────────────────────────────────────────────

public class SemanticAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public string AnalysisType { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public int TotalIssues { get; set; }
    public List<SavedIssueDto> Issues { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
