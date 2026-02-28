using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class CommentExplorerService : ICommentExplorerService
{
    private readonly ILogger<CommentExplorerService> _logger;

    public CommentExplorerService(ILogger<CommentExplorerService> logger) => _logger = logger;

    public async Task<CommentExplorerResultDto> GetAllCommentsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var result = new CommentExplorerResultDto { SolutionPath = solutionPath };
        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);
            using (workspace)
            {
                foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var root = await doc.GetSyntaxRootAsync(cancellationToken);
                    if (root is null) continue;
                    var filePath = doc.FilePath ?? doc.Name;
                    var fileName = doc.Name;
                    result.Comments.AddRange(ExtractComments(root, filePath, fileName));
                    result.Regions.AddRange(ExtractRegions(root, filePath, fileName));
                }
            }
            result.TotalComments  = result.Comments.Count;
            result.TotalRegions   = result.Regions.Count;
            result.XmlDocComments = result.Comments.Count(c => c.CommentType == "XmlDoc");
            result.TodoComments   = result.Comments.Count(c =>
                c.Content.Contains("TODO",  StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains("FIXME", StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains("HACK",  StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exploring comments in {Path}", solutionPath);
            result.Errors.Add($"Comment explorer error: {ex.Message}");
        }
        return result;
    }

    public async Task<List<CommentDto>> GetTodoCommentsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var all = await GetAllCommentsAsync(solutionPath, cancellationToken);
        return all.Comments.Where(c =>
            c.Content.Contains("TODO",  StringComparison.OrdinalIgnoreCase) ||
            c.Content.Contains("FIXME", StringComparison.OrdinalIgnoreCase) ||
            c.Content.Contains("HACK",  StringComparison.OrdinalIgnoreCase) ||
            c.Content.Contains("NOTE",  StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<List<RegionDto>> GetRegionsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var all = await GetAllCommentsAsync(solutionPath, cancellationToken);
        return all.Regions;
    }

    private static List<CommentDto> ExtractComments(SyntaxNode root, string filePath, string fileName)
    {
        var comments = new List<CommentDto>();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            var lineNumber = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                    comments.Add(new CommentDto
                    {
                        FilePath = filePath, FileName = fileName, LineNumber = lineNumber,
                        CommentType = "SingleLine",
                        Content = trivia.ToString().TrimStart('/').Trim(),
                        AssociatedMember = FindAssociatedMember(trivia)
                    });
                    break;
                case SyntaxKind.MultiLineCommentTrivia:
                    comments.Add(new CommentDto
                    {
                        FilePath = filePath, FileName = fileName, LineNumber = lineNumber,
                        CommentType = "MultiLine",
                        Content = trivia.ToString().TrimStart('/').TrimStart('*').TrimEnd('/').TrimEnd('*').Trim(),
                        AssociatedMember = FindAssociatedMember(trivia)
                    });
                    break;
                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                    comments.Add(new CommentDto
                    {
                        FilePath = filePath, FileName = fileName, LineNumber = lineNumber,
                        CommentType = "XmlDoc",
                        Content = trivia.ToString().Replace("///", "").Trim(),
                        AssociatedMember = FindAssociatedMember(trivia)
                    });
                    break;
            }
        }
        return comments;
    }

    private static List<RegionDto> ExtractRegions(SyntaxNode root, string filePath, string fileName)
    {
        var regions     = new List<RegionDto>();
        var regionStack = new Stack<(string name, int startLine)>();

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                var lineNumber  = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var regionName  = trivia.ToString().Replace("#region", "").Trim();
                regionStack.Push((regionName, lineNumber));
            }
            else if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia) && regionStack.Count > 0)
            {
                var (name, startLine) = regionStack.Pop();
                var endLine = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                regions.Add(new RegionDto
                {
                    FilePath    = filePath, FileName = fileName,
                    RegionName  = name,  StartLine = startLine,
                    EndLine     = endLine, LinesOfCode = endLine - startLine + 1
                });
            }
        }
        return regions;
    }

    private static string? FindAssociatedMember(SyntaxTrivia trivia)
    {
        var node   = trivia.Token.Parent;
        var method = node?.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null) return method.Identifier.Text;
        var type = node?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return type?.Identifier.Text;
    }
}
