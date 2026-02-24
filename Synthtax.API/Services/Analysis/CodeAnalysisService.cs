using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis;

public class CodeAnalysisService : ICodeAnalysisService
{
    private readonly ILogger<CodeAnalysisService> _logger;

    public CodeAnalysisService(ILogger<CodeAnalysisService> logger)
    {
        _logger = logger;
    }

    public async Task<CodeAnalysisResultDto> AnalyzeSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var result = new CodeAnalysisResultDto { SolutionPath = solutionPath };

        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);

            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();

                foreach (var doc in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var root = await doc.GetSyntaxRootAsync(cancellationToken);
                    var semanticModel = await doc.GetSemanticModelAsync(cancellationToken);
                    if (root is null || semanticModel is null) continue;

                    result.LongMethods.AddRange(FindLongMethodsInTree(root, doc.FilePath ?? doc.Name));
                    result.DeadVariables.AddRange(FindDeadVariablesInTree(root, semanticModel, doc.FilePath ?? doc.Name));
                    result.UnnecessaryUsings.AddRange(FindUnnecessaryUsingsInTree(root, semanticModel, doc.FilePath ?? doc.Name));
                }
            }

            result.TotalIssues = result.LongMethods.Count
                               + result.DeadVariables.Count
                               + result.UnnecessaryUsings.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing solution {Path}", solutionPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    public async Task<CodeAnalysisResultDto> AnalyzeProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var result = new CodeAnalysisResultDto { SolutionPath = projectPath };

        try
        {
            var (workspace, project) = await RoslynWorkspaceHelper.LoadProjectAsync(
                projectPath, _logger, cancellationToken);

            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(project).ToList();

                foreach (var doc in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var root = await doc.GetSyntaxRootAsync(cancellationToken);
                    var semanticModel = await doc.GetSemanticModelAsync(cancellationToken);
                    if (root is null || semanticModel is null) continue;

                    result.LongMethods.AddRange(FindLongMethodsInTree(root, doc.FilePath ?? doc.Name));
                    result.DeadVariables.AddRange(FindDeadVariablesInTree(root, semanticModel, doc.FilePath ?? doc.Name));
                    result.UnnecessaryUsings.AddRange(FindUnnecessaryUsingsInTree(root, semanticModel, doc.FilePath ?? doc.Name));
                }
            }

            result.TotalIssues = result.LongMethods.Count
                               + result.DeadVariables.Count
                               + result.UnnecessaryUsings.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project {Path}", projectPath);
            result.Errors.Add($"Analysis error: {ex.Message}");
        }

        return result;
    }

    public async Task<List<CodeIssueDto>> FindLongMethodsAsync(
        string solutionPath,
        int maxLines = 50,
        CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<CodeIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                if (root is null) continue;
                results.AddRange(FindLongMethodsInTree(root, doc.FilePath ?? doc.Name, maxLines));
            }
        }
        return results;
    }

    public async Task<List<CodeIssueDto>> FindDeadVariablesAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<CodeIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                var model = await doc.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null) continue;
                results.AddRange(FindDeadVariablesInTree(root, model, doc.FilePath ?? doc.Name));
            }
        }
        return results;
    }

    public async Task<List<CodeIssueDto>> FindUnnecessaryUsingsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<CodeIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                var model = await doc.GetSemanticModelAsync(cancellationToken);
                if (root is null || model is null) continue;
                results.AddRange(FindUnnecessaryUsingsInTree(root, model, doc.FilePath ?? doc.Name));
            }
        }
        return results;
    }

    // ── Private Analysis Helpers ─────────────────────────────────────────────

    private static List<CodeIssueDto> FindLongMethodsInTree(
        SyntaxNode root,
        string filePath,
        int maxLines = 50)
    {
        var issues = new List<CodeIssueDto>();
        var fileName = Path.GetFileName(filePath);

        var methods = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            var span = method.GetLocation().GetLineSpan();
            var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;

            if (lineCount <= maxLines) continue;

            var containingClass = method.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.Text ?? "Unknown";

            issues.Add(new CodeIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "LongMethod",
                Description = $"Method '{method.Identifier.Text}' is {lineCount} lines long (limit: {maxLines}).",
                LineNumber = span.StartLinePosition.Line + 1,
                LineCount = lineCount,
                MethodName = method.Identifier.Text,
                Snippet = $"{containingClass}.{method.Identifier.Text}",
                Severity = lineCount > maxLines * 2 ? Severity.High : Severity.Medium
            });
        }

        return issues;
    }

    private static List<CodeIssueDto> FindDeadVariablesInTree(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath)
    {
        var issues = new List<CodeIssueDto>();
        var fileName = Path.GetFileName(filePath);

        // Find all local variable declarations
        var localDeclarations = root.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>();

        foreach (var declaration in localDeclarations)
        {
            foreach (var variable in declaration.Declaration.Variables)
            {
                var symbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                if (symbol is null) continue;

                // Find all references to this symbol in its containing method body
                var containingMethod = declaration.Ancestors()
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault();

                if (containingMethod is null) continue;

                var methodRoot = containingMethod.Body ?? (SyntaxNode?)containingMethod.ExpressionBody;
                if (methodRoot is null) continue;

                // Count usages (excluding the declaration itself)
                var usages = methodRoot.DescendantNodes()
                    .OfType<IdentifierNameSyntax>()
                    .Where(id => id.Identifier.Text == variable.Identifier.Text
                              && id.SpanStart != variable.Identifier.SpanStart)
                    .ToList();

                if (usages.Count == 0)
                {
                    var span = variable.GetLocation().GetLineSpan();
                    issues.Add(new CodeIssueDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        IssueType = "DeadVariable",
                        Description = $"Variable '{variable.Identifier.Text}' is declared but never used.",
                        LineNumber = span.StartLinePosition.Line + 1,
                        MethodName = containingMethod.Identifier.Text,
                        Snippet = declaration.ToString().Trim(),
                        Severity = Severity.Low
                    });
                }
            }
        }

        return issues;
    }

    private static List<CodeIssueDto> FindUnnecessaryUsingsInTree(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath)
    {
        var issues = new List<CodeIssueDto>();
        var fileName = Path.GetFileName(filePath);

        var usingDirectives = root.DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Where(u => u.StaticKeyword.IsKind(SyntaxKind.None) && u.Alias is null);

        // Collect all referenced type names in this file
        var referencedIdentifiers = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .ToHashSet();

        var referencedQualified = root.DescendantNodes()
            .OfType<QualifiedNameSyntax>()
            .Select(q => q.ToString())
            .ToHashSet();

        foreach (var usingDir in usingDirectives)
        {
            if (usingDir.Name is null) continue;

            var namespaceName = usingDir.Name.ToString();

            // Check if any referenced identifier could belong to this namespace
            // by looking for unqualified identifiers that reference symbols in this namespace
            var isUsed = false;

            foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
                if (symbol is null) continue;

                var ns = symbol.ContainingNamespace?.ToDisplayString();
                if (ns is not null && (ns == namespaceName || ns.StartsWith(namespaceName + ".")))
                {
                    isUsed = true;
                    break;
                }
            }

            if (!isUsed)
            {
                var span = usingDir.GetLocation().GetLineSpan();
                issues.Add(new CodeIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "UnnecessaryUsing",
                    Description = $"Using directive '{namespaceName}' appears to be unnecessary.",
                    LineNumber = span.StartLinePosition.Line + 1,
                    Snippet = usingDir.ToString().Trim(),
                    Severity = Severity.Low
                });
            }
        }

        return issues;
    }
}
