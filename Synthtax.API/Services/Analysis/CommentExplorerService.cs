using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis;

public class CommentExplorerService : ICommentExplorerService
{
    private readonly ILogger<CommentExplorerService> _logger;

    public CommentExplorerService(ILogger<CommentExplorerService> logger)
    {
        _logger = logger;
    }

    public async Task<CommentExplorerResultDto> GetAllCommentsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
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

            result.TotalComments = result.Comments.Count;
            result.TotalRegions = result.Regions.Count;
            result.XmlDocComments = result.Comments.Count(c => c.CommentType == "XmlDoc");
            result.TodoComments = result.Comments.Count(c =>
                c.Content.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains("FIXME", StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains("HACK", StringComparison.OrdinalIgnoreCase));
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
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllCommentsAsync(solutionPath, cancellationToken);
        return all.Comments
            .Where(c => c.Content.Contains("TODO", StringComparison.OrdinalIgnoreCase)
                     || c.Content.Contains("FIXME", StringComparison.OrdinalIgnoreCase)
                     || c.Content.Contains("HACK", StringComparison.OrdinalIgnoreCase)
                     || c.Content.Contains("NOTE", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<List<RegionDto>> GetRegionsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllCommentsAsync(solutionPath, cancellationToken);
        return all.Regions;
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private static List<CommentDto> ExtractComments(
        SyntaxNode root,
        string filePath,
        string fileName)
    {
        var comments = new List<CommentDto>();

        var triviaList = root.DescendantTrivia(descendIntoTrivia: true);

        foreach (var trivia in triviaList)
        {
            var lineSpan = trivia.GetLocation().GetLineSpan();
            var lineNumber = lineSpan.StartLinePosition.Line + 1;

            switch (trivia.Kind())
            {
                case SyntaxKind.SingleLineCommentTrivia:
                {
                    var content = trivia.ToString().TrimStart('/').Trim();
                    comments.Add(new CommentDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        LineNumber = lineNumber,
                        CommentType = "SingleLine",
                        Content = content,
                        AssociatedMember = FindAssociatedMember(trivia)
                    });
                    break;
                }

                case SyntaxKind.MultiLineCommentTrivia:
                {
                    var raw = trivia.ToString();
                    var content = raw.TrimStart('/').TrimStart('*').TrimEnd('/').TrimEnd('*').Trim();
                    comments.Add(new CommentDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        LineNumber = lineNumber,
                        CommentType = "MultiLine",
                        Content = content,
                        AssociatedMember = FindAssociatedMember(trivia)
                    });
                    break;
                }

                case SyntaxKind.SingleLineDocumentationCommentTrivia:
                case SyntaxKind.MultiLineDocumentationCommentTrivia:
                {
                    var content = trivia.ToString()
                        .Replace("///", "")
                        .Replace("/**", "")
                        .Replace("*/", "")
                        .Trim();
                    comments.Add(new CommentDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        LineNumber = lineNumber,
                        CommentType = "XmlDoc",
                        Content = content,
                        AssociatedMember = FindAssociatedMember(trivia)
                    });
                    break;
                }
            }
        }

        return comments;
    }

    private static List<RegionDto> ExtractRegions(
        SyntaxNode root,
        string filePath,
        string fileName)
    {
        var regions = new List<RegionDto>();
        var regionStack = new Stack<(string name, int startLine)>();

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                var lineSpan = trivia.GetLocation().GetLineSpan();
                var lineNumber = lineSpan.StartLinePosition.Line + 1;
                var text = trivia.ToString();
                var regionName = text.Replace("#region", "").Trim();
                regionStack.Push((regionName, lineNumber));
            }
            else if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia) && regionStack.Count > 0)
            {
                var (name, startLine) = regionStack.Pop();
                var endLine = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                regions.Add(new RegionDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    RegionName = name,
                    StartLine = startLine,
                    EndLine = endLine,
                    LinesOfCode = endLine - startLine + 1
                });
            }
        }

        return regions;
    }

    private static string? FindAssociatedMember(SyntaxTrivia trivia)
    {
        var token = trivia.Token;
        var node = token.Parent;

        var method = node?.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is not null) return method.Identifier.Text;

        var type = node?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type is not null) return type.Identifier.Text;

        return null;
    }
}
