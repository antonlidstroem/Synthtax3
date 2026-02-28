using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class MetricsService : IMetricsService
{
    private readonly ILogger<MetricsService> _logger;

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
    }

    public async Task<MetricsResultDto> AnalyzeSolutionMetricsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var result = new MetricsResultDto { SolutionPath = solutionPath };

        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);

            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(solution).ToList();
                var bag = new ConcurrentBag<FileMetricsDto>();

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var fileMetrics = await AnalyzeDocumentAsync(doc, ct);
                        if (fileMetrics is not null)
                            bag.Add(fileMetrics);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Metrics failed for {Doc}", doc.Name);
                    }
                });

                result.Files.AddRange(bag);
            }

            AggregateResults(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing metrics for solution {Path}", solutionPath);
            result.Errors.Add($"Metrics error: {ex.Message}");
        }

        return result;
    }

    public async Task<MetricsResultDto> AnalyzeProjectMetricsAsync(
        string projectPath, CancellationToken cancellationToken = default)
    {
        var result = new MetricsResultDto { SolutionPath = projectPath };

        try
        {
            var (workspace, project) = await RoslynWorkspaceHelper.LoadProjectAsync(
                projectPath, _logger, cancellationToken);

            using (workspace)
            {
                var documents = RoslynWorkspaceHelper.GetCSharpDocuments(project).ToList();
                var bag = new ConcurrentBag<FileMetricsDto>();

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount / 2),
                    CancellationToken = cancellationToken
                };

                await Parallel.ForEachAsync(documents, parallelOptions, async (doc, ct) =>
                {
                    try
                    {
                        var fileMetrics = await AnalyzeDocumentAsync(doc, ct);
                        if (fileMetrics is not null)
                            bag.Add(fileMetrics);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Metrics failed for {Doc}", doc.Name);
                    }
                });

                result.Files.AddRange(bag);
            }

            AggregateResults(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing project metrics {Path}", projectPath);
            result.Errors.Add($"Metrics error: {ex.Message}");
        }

        return result;
    }

    public async Task<FileMetricsDto> AnalyzeFileMetricsAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var code = await File.ReadAllTextAsync(filePath, cancellationToken);
        var tree = CSharpSyntaxTree.ParseText(code, path: filePath, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);

        return ComputeFileMetrics(root, filePath, Path.GetFileName(filePath), "Standalone");
    }

    // ─────────────────────────────────────────────────────────────

    private static async Task<FileMetricsDto?> AnalyzeDocumentAsync(
        Document doc, CancellationToken cancellationToken)
    {
        var root = await doc.GetSyntaxRootAsync(cancellationToken);
        if (root is null)
            return null;

        return ComputeFileMetrics(
            root,
            doc.FilePath ?? doc.Name,
            doc.Name,
            doc.Project.Name);
    }

    private static FileMetricsDto ComputeFileMetrics(
        SyntaxNode root,
        string filePath,
        string fileName,
        string projectName)
    {
        var lines = root.GetText().Lines;

        int linesOfCode = 0;
        int linesOfComments = 0;
        int blankLines = 0;

        foreach (var line in lines)
        {
            var text = line.ToString().Trim();

            if (string.IsNullOrWhiteSpace(text))
                blankLines++;
            else if (text.StartsWith("//") || text.StartsWith("/*") || text.StartsWith("*"))
                linesOfComments++;
            else
                linesOfCode++;
        }

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var methodMetrics = methods.Select(ComputeMethodMetrics).ToList();

        var avgComplexity = methodMetrics.Count > 0
            ? methodMetrics.Average(m => m.CyclomaticComplexity)
            : 1.0;

        var maintainability = ComputeFileMaintainabilityIndex(
            linesOfCode,
            avgComplexity,
            linesOfComments);

        return new FileMetricsDto
        {
            FilePath = filePath,
            FileName = fileName,
            ProjectName = projectName,
            LinesOfCode = linesOfCode,
            LinesOfComments = linesOfComments,
            BlankLines = blankLines,
            AverageCyclomaticComplexity = Math.Round(avgComplexity, 2),
            MaintainabilityIndex = Math.Round(maintainability, 2),
            NumberOfMethods = methods.Count,
            NumberOfClasses = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count(),
            Methods = methodMetrics
        };
    }

    private static MethodMetricsDto ComputeMethodMetrics(MethodDeclarationSyntax method)
    {
        var span = method.GetLocation().GetLineSpan();
        var loc = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;

        var complexity = ComputeCyclomaticComplexity(method);
        var cognitiveComplexity = CognitiveComplexityCalculator.Calculate(method, method.Identifier.Text);
        var maintainability = ComputeMethodMaintainabilityIndex(loc, complexity);

        var containingClass = method.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()?.Identifier.Text ?? "Unknown";

        return new MethodMetricsDto
        {
            MethodName = method.Identifier.Text,
            ClassName = containingClass,
            LineNumber = span.StartLinePosition.Line + 1,
            LinesOfCode = loc,
            CyclomaticComplexity = complexity,
            CognitiveComplexity = cognitiveComplexity,
            MaintainabilityIndex = Math.Round(maintainability, 2)
        };
    }

    // ✅ FIXAD VERSION
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

    private static double ComputeMethodMaintainabilityIndex(int loc, int complexity)
    {
        if (loc <= 0)
            return 100;

        var mi = 171
                 - 5.2 * Math.Log(Math.Max(1, loc))
                 - 0.23 * complexity
                 - 16.2 * Math.Log(Math.Max(1, loc));

        return Math.Clamp(mi * 100.0 / 171.0, 0, 100);
    }

    private static double ComputeFileMaintainabilityIndex(
        int loc,
        double avgComplexity,
        int commentLines)
    {
        if (loc <= 0)
            return 100;

        var commentRatio = (double)commentLines / (loc + commentLines);

        var mi = 171
                 - 5.2 * Math.Log(Math.Max(1, loc))
                 - 0.23 * avgComplexity
                 - 16.2 * Math.Log(Math.Max(1, loc))
                 + 50 * Math.Sin(Math.Sqrt(2.4 * commentRatio));

        return Math.Clamp(mi * 100.0 / 171.0, 0, 100);
    }

    private static void AggregateResults(MetricsResultDto result)
    {
        result.TotalFiles = result.Files.Count;
        result.TotalLinesOfCode = result.Files.Sum(f => f.LinesOfCode);
        result.TotalMethods = result.Files.Sum(f => f.NumberOfMethods);

        if (result.Files.Count > 0)
        {
            result.OverallMaintainabilityIndex =
                Math.Round(result.Files.Average(f => f.MaintainabilityIndex), 2);

            result.OverallCyclomaticComplexity =
                Math.Round(result.Files.Average(f => f.AverageCyclomaticComplexity), 2);
        }
    }

    public Task<List<MetricsTrendPointDto>> GetMetricsTrendAsync(
        string solutionPath,
        int maxDataPoints = 30,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}