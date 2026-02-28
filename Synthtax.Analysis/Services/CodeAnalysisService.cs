using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Pipeline;
using Synthtax.Analysis.Rules;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class CodeAnalysisService : ICodeAnalysisService, IContextAwareAnalysis
{
    private readonly ILogger<CodeAnalysisService> _logger;
    private readonly IRoslynWorkspaceService _workspace;
    private readonly IReadOnlyList<IAnalysisRule<CodeIssueDto>> _rules;

    public CodeAnalysisService(
        ILogger<CodeAnalysisService> logger,
        IRoslynWorkspaceService workspace,
        IEnumerable<IAnalysisRule<CodeIssueDto>>? rules = null)
    {
        _logger    = logger;
        _workspace = workspace;
        _rules     = rules?.Where(r => r.IsEnabled).ToList()
                     ?? new List<IAnalysisRule<CodeIssueDto>>
                     {
                         new LongMethodRule(),
                         new DeadVariableRule(),
                         new UnnecessaryUsingRule(),
                     };
    }

    public async Task<object> AnalyzeAsync(AnalysisContext ctx, CancellationToken ct)
        => await RunRulesOnContext(ctx, ctx.Solution.FilePath ?? "solution", ct);

    public async Task<CodeAnalysisResultDto> AnalyzeSolutionAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await _workspace.LoadSolutionAsync(solutionPath, cancellationToken);
        await using var ctx = await AnalysisContext.BuildAsync(
            solution, workspace, _workspace, null, _logger, cancellationToken);
        return await RunRulesOnContext(ctx, solutionPath, cancellationToken);
    }

    public async Task<CodeAnalysisResultDto> AnalyzeProjectAsync(
        string projectPath, CancellationToken cancellationToken = default)
    {
        var (workspace, project) = await _workspace.LoadProjectAsync(projectPath, cancellationToken);
        var result = new CodeAnalysisResultDto { SolutionPath = projectPath };
        try
        {
            var docs = _workspace.GetCSharpDocuments(project).ToList();
            await ProcessDocumentsAsync(docs, null, result, cancellationToken);
            result.TotalIssues =
                result.LongMethods.Count + result.DeadVariables.Count + result.UnnecessaryUsings.Count;
        }
        finally { workspace.Dispose(); }
        return result;
    }

    public async Task<List<CodeIssueDto>> FindLongMethodsAsync(
        string solutionPath, int maxLines = 50, CancellationToken cancellationToken = default)
        => await RunSingleRule(solutionPath, new LongMethodRule(maxLines), cancellationToken);

    public async Task<List<CodeIssueDto>> FindDeadVariablesAsync(
        string solutionPath, CancellationToken cancellationToken = default)
        => await RunSingleRule(solutionPath, new DeadVariableRule(), cancellationToken);

    public async Task<List<CodeIssueDto>> FindUnnecessaryUsingsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
        => await RunSingleRule(solutionPath, new UnnecessaryUsingRule(), cancellationToken);

    // ── private helpers ────────────────────────────────────────────────────────

    private async Task<CodeAnalysisResultDto> RunRulesOnContext(
        AnalysisContext ctx, string solutionPath, CancellationToken ct)
    {
        var result = new CodeAnalysisResultDto { SolutionPath = solutionPath };
        try
        {
            await ProcessDocumentsAsync(ctx.Documents, ctx, result, ct);
            result.TotalIssues =
                result.LongMethods.Count + result.DeadVariables.Count + result.UnnecessaryUsings.Count;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Code analysis error for {Path}", solutionPath);
            result.Errors.Add($"Code analysis error: {ex.Message}");
        }
        return result;
    }

    private async Task ProcessDocumentsAsync(
        IReadOnlyList<Document> docs,
        AnalysisContext? ctx,
        CodeAnalysisResultDto result,
        CancellationToken ct)
    {
        var longBag  = new ConcurrentBag<CodeIssueDto>();
        var deadBag  = new ConcurrentBag<CodeIssueDto>();
        var usingBag = new ConcurrentBag<CodeIssueDto>();

        await Parallel.ForEachAsync(docs,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (doc, token) =>
            {
                var root    = ctx?.GetRoot(doc)  ?? await doc.GetSyntaxRootAsync(token);
                var model   = ctx?.GetModel(doc) ?? await doc.GetSemanticModelAsync(token);
                if (root is null) return;
                var filePath = ctx?.GetFilePath(doc) ?? doc.FilePath ?? doc.Name;

                foreach (var rule in _rules)
                foreach (var issue in rule.Analyze(root, model, filePath, token))
                {
                    switch (issue.IssueType)
                    {
                        case "LongMethod":        longBag.Add(issue);  break;
                        case "DeadVariable":       deadBag.Add(issue);  break;
                        case "UnnecessaryUsing":   usingBag.Add(issue); break;
                    }
                }
            });

        result.LongMethods.AddRange(longBag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber));
        result.DeadVariables.AddRange(deadBag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber));
        result.UnnecessaryUsings.AddRange(usingBag.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber));
    }

    private async Task<List<CodeIssueDto>> RunSingleRule(
        string solutionPath, IAnalysisRule<CodeIssueDto> rule, CancellationToken ct)
    {
        var (ws, sol) = await _workspace.LoadSolutionAsync(solutionPath, ct);
        var results   = new ConcurrentBag<CodeIssueDto>();
        await using var ctx = await AnalysisContext.BuildAsync(sol, ws, _workspace, null, _logger, ct);
        await Parallel.ForEachAsync(ctx.Documents,
            new ParallelOptions { CancellationToken = ct },
            (doc, token) =>
            {
                var root  = ctx.GetRoot(doc);
                var model = ctx.GetModel(doc);
                if (root is null) return ValueTask.CompletedTask;
                foreach (var issue in rule.Analyze(root, model, ctx.GetFilePath(doc), token))
                    results.Add(issue);
                return ValueTask.CompletedTask;
            });
        return results.ToList();
    }
}
