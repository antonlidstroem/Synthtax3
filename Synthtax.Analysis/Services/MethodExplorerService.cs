using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class MethodExplorerService : IMethodExplorerService
{
    private readonly ILogger<MethodExplorerService> _logger;

    public MethodExplorerService(ILogger<MethodExplorerService> logger) => _logger = logger;

    public async Task<MethodExplorerResultDto> GetAllMethodsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var result = new MethodExplorerResultDto { SolutionPath = solutionPath };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var root  = await doc.GetSyntaxRootAsync(cancellationToken);
                    var model = await doc.GetSemanticModelAsync(cancellationToken);
                    if (root is null || model is null) continue;
                    result.Methods.AddRange(ExtractMethods(root, model, doc.FilePath ?? doc.Name, doc.Name));
                }
            }
            result.TotalMethods = result.Methods.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exploring methods in {Path}", solutionPath);
            result.Errors.Add($"Method explorer error: {ex.Message}");
        }
        return result;
    }

    public async Task<List<MethodDto>> SearchMethodsAsync(
        string solutionPath, string searchPattern, CancellationToken cancellationToken = default)
    {
        var all = await GetAllMethodsAsync(solutionPath, cancellationToken);
        return all.Methods.Where(m =>
            m.MethodName.Contains(searchPattern, StringComparison.OrdinalIgnoreCase) ||
            m.FullSignature.Contains(searchPattern, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<MethodDto>> GetMethodsForClassAsync(
        string solutionPath, string className, CancellationToken cancellationToken = default)
    {
        var all = await GetAllMethodsAsync(solutionPath, cancellationToken);
        return all.Methods
            .Where(m => m.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<MethodDto> ExtractMethods(
        SyntaxNode root, SemanticModel model, string filePath, string fileName)
    {
        var methods = new List<MethodDto>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var span      = method.GetLocation().GetLineSpan();
            var startLine = span.StartLinePosition.Line + 1;
            var endLine   = span.EndLinePosition.Line + 1;

            var containingClass = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var ns = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString()
                  ?? method.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString()
                  ?? string.Empty;

            var modifiers   = method.Modifiers.Select(m => m.Text).ToList();
            var parameters  = method.ParameterList.Parameters.Select(p => p.ToString()).ToList();
            var complexity   = ComputeCyclomaticComplexity(method);
            var symbolInfo  = model.GetDeclaredSymbol(method);
            var xmlDoc      = symbolInfo?.GetDocumentationCommentXml();
            string? xmlSummary = null;

            if (!string.IsNullOrEmpty(xmlDoc))
            {
                var start = xmlDoc.IndexOf("<summary>",  StringComparison.OrdinalIgnoreCase);
                var end   = xmlDoc.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
                if (start >= 0 && end > start) xmlSummary = xmlDoc[(start + 9)..end].Trim();
            }

            methods.Add(new MethodDto
            {
                MethodName       = method.Identifier.Text,
                FullSignature    = BuildFullSignature(method),
                ClassName        = containingClass?.Identifier.Text ?? "Global",
                NamespaceName    = ns,
                FilePath         = filePath, FileName = fileName,
                StartLine        = startLine, EndLine = endLine,
                LinesOfCode      = endLine - startLine + 1,
                CyclomaticComplexity = complexity,
                ReturnType       = method.ReturnType.ToString(),
                Parameters       = parameters, Modifiers = modifiers,
                IsAsync          = modifiers.Contains("async"),
                IsStatic         = modifiers.Contains("static"),
                IsPublic         = modifiers.Contains("public"),
                XmlDocSummary    = xmlSummary
            });
        }
        return methods;
    }

    private static string BuildFullSignature(MethodDeclarationSyntax method)
    {
        var modifiers   = string.Join(" ", method.Modifiers.Select(m => m.Text));
        var typeParams  = method.TypeParameterList?.ToString() ?? string.Empty;
        return $"{modifiers} {method.ReturnType} {method.Identifier.Text}{typeParams}{method.ParameterList}".Trim();
    }

    private static int ComputeCyclomaticComplexity(SyntaxNode method)
    {
        var decisionPoints = method.DescendantNodes().Count(node =>
            node is IfStatementSyntax
                or WhileStatementSyntax
                or ForStatementSyntax
                or ForEachStatementSyntax
                or SwitchSectionSyntax
                or CatchClauseSyntax
                or ConditionalExpressionSyntax
                or SwitchExpressionArmSyntax
            || (node is BinaryExpressionSyntax be &&
                (be.IsKind(SyntaxKind.LogicalAndExpression) ||
                 be.IsKind(SyntaxKind.LogicalOrExpression)))
        );

        return decisionPoints + 1;
    }
}
