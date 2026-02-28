using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class StructureAnalysisService : IStructureAnalysisService
{
    private readonly ILogger<StructureAnalysisService> _logger;

    public StructureAnalysisService(ILogger<StructureAnalysisService> logger) => _logger = logger;

    public async Task<StructureAnalysisResultDto> AnalyzeSolutionStructureAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var result = new StructureAnalysisResultDto { SolutionPath = solutionPath };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                var solutionNode = new StructureNodeDto
                {
                    Name     = Path.GetFileNameWithoutExtension(solutionPath),
                    NodeType = "Solution"
                };

                foreach (var project in solution.Projects.OrderBy(p => p.Name))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var projectNode    = new StructureNodeDto { Name = project.Name, NodeType = "Project" };
                    var namespaceMap   = new Dictionary<string, StructureNodeDto>();

                    foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(project).OrderBy(d => d.Name))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var root = await doc.GetSyntaxRootAsync(cancellationToken);
                        if (root is null) continue;
                        ExtractNamespacesAndTypes(root, doc.FilePath ?? doc.Name, projectNode, namespaceMap);
                    }
                    solutionNode.Children.Add(projectNode);
                }
                result.RootNode = solutionNode;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing structure of {Path}", solutionPath);
            result.Errors.Add($"Structure analysis error: {ex.Message}");
        }
        return result;
    }

    private static void ExtractNamespacesAndTypes(
        SyntaxNode root, string filePath,
        StructureNodeDto projectNode, Dictionary<string, StructureNodeDto> namespaceMap)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (typeDecl.Parent is TypeDeclarationSyntax) continue;

            var nsName = GetContainingNamespaceName(typeDecl) ?? "Global";
            if (!namespaceMap.TryGetValue(nsName, out var nsNode))
            {
                nsNode = new StructureNodeDto { Name = nsName, NodeType = "Namespace" };
                namespaceMap[nsName] = nsNode;
                projectNode.Children.Add(nsNode);
            }

            var span     = typeDecl.GetLocation().GetLineSpan();
            var typeNode = new StructureNodeDto
            {
                Name       = typeDecl.Identifier.Text,
                NodeType   = typeDecl switch
                {
                    ClassDeclarationSyntax     => "Class",
                    InterfaceDeclarationSyntax => "Interface",
                    StructDeclarationSyntax    => "Struct",
                    RecordDeclarationSyntax    => "Record",
                    _                          => "Type"
                },
                FilePath   = filePath,
                LineNumber = span.StartLinePosition.Line + 1,
                Modifier   = string.Join(" ", typeDecl.Modifiers.Select(m => m.Text)),
                IsAbstract = typeDecl.Modifiers.Any(m => m.Text == "abstract"),
                IsStatic   = typeDecl.Modifiers.Any(m => m.Text == "static")
            };

            foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                var mSpan = method.GetLocation().GetLineSpan();
                typeNode.Children.Add(new StructureNodeDto
                {
                    Name       = method.Identifier.Text, NodeType = "Method",
                    FilePath   = filePath, LineNumber = mSpan.StartLinePosition.Line + 1,
                    Modifier   = string.Join(" ", method.Modifiers.Select(m => m.Text)),
                    ReturnType = method.ReturnType.ToString(),
                    IsStatic   = method.Modifiers.Any(m => m.Text == "static")
                });
            }

            foreach (var prop in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            {
                var pSpan = prop.GetLocation().GetLineSpan();
                typeNode.Children.Add(new StructureNodeDto
                {
                    Name = prop.Identifier.Text, NodeType = "Property",
                    FilePath = filePath, LineNumber = pSpan.StartLinePosition.Line + 1,
                    Modifier = string.Join(" ", prop.Modifiers.Select(m => m.Text)),
                    ReturnType = prop.Type.ToString()
                });
            }

            foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            foreach (var variable in field.Declaration.Variables)
            {
                var fSpan = field.GetLocation().GetLineSpan();
                typeNode.Children.Add(new StructureNodeDto
                {
                    Name = variable.Identifier.Text, NodeType = "Field",
                    FilePath = filePath, LineNumber = fSpan.StartLinePosition.Line + 1,
                    Modifier = string.Join(" ", field.Modifiers.Select(m => m.Text)),
                    ReturnType = field.Declaration.Type.ToString()
                });
            }

            nsNode.Children.Add(typeNode);
        }
    }

    private static string? GetContainingNamespaceName(TypeDeclarationSyntax typeDecl)
    {
        foreach (var ancestor in typeDecl.Ancestors())
        {
            if (ancestor is NamespaceDeclarationSyntax ns)           return ns.Name.ToString();
            if (ancestor is FileScopedNamespaceDeclarationSyntax fns) return fns.Name.ToString();
            if (ancestor is CompilationUnitSyntax)                    break;
        }
        return null;
    }
}
