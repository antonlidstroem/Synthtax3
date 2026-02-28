using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Pipeline;
using Synthtax.Analysis.Rules;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class SecurityAnalysisService : ISecurityAnalysisService, IContextAwareAnalysis
{
    private readonly ILogger<SecurityAnalysisService> _logger;
    private readonly IRoslynWorkspaceService _workspace;
    private readonly IReadOnlyList<IAnalysisRule<SecurityIssueDto>> _rules;

    public SecurityAnalysisService(
        ILogger<SecurityAnalysisService> logger,
        IRoslynWorkspaceService workspace,
        IEnumerable<IAnalysisRule<SecurityIssueDto>>? rules = null)
    {
        _logger    = logger;
        _workspace = workspace;
        _rules     = rules?.Where(r => r.IsEnabled).ToList()
                     ?? new List<IAnalysisRule<SecurityIssueDto>>
                     {
                         new HardcodedCredentialRule(),
                         new SqlInjectionRule(),
                         new InsecureRandomRule(),
                         new MissingCancellationTokenRule(),
                     };
    }

    public async Task<object> AnalyzeAsync(AnalysisContext ctx, CancellationToken ct)
        => await RunRulesOnContext(ctx, ctx.Solution.FilePath ?? "solution", ct);

    public async Task<SecurityAnalysisResultDto> AnalyzeSolutionAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (ws, sol) = await _workspace.LoadSolutionAsync(solutionPath, cancellationToken);
        await using var ctx = await AnalysisContext.BuildAsync(
            sol, ws, _workspace, null, _logger, cancellationToken);
        return await RunRulesOnContext(ctx, solutionPath, cancellationToken);
    }

    public Task<List<SecurityIssueDto>> FindHardcodedCredentialsAsync(
        string solutionPath, CancellationToken ct = default)
        => RunSingleRuleAsync(solutionPath, new HardcodedCredentialRule(), ct);

    public Task<List<SecurityIssueDto>> FindSqlInjectionRisksAsync(
        string solutionPath, CancellationToken ct = default)
        => RunSingleRuleAsync(solutionPath, new SqlInjectionRule(), ct);

    public Task<List<SecurityIssueDto>> FindInsecureRandomUsageAsync(
        string solutionPath, CancellationToken ct = default)
        => RunSingleRuleAsync(solutionPath, new InsecureRandomRule(), ct);

    public Task<List<SecurityIssueDto>> FindMissingCancellationTokensAsync(
        string solutionPath, CancellationToken ct = default)
        => RunSingleRuleAsync(solutionPath, new MissingCancellationTokenRule(), ct);

    // ── private helpers ────────────────────────────────────────────────────────

    private async Task<SecurityAnalysisResultDto> RunRulesOnContext(
        AnalysisContext ctx, string solutionPath, CancellationToken ct)
    {
        var result = new SecurityAnalysisResultDto { SolutionPath = solutionPath };
        try
        {
            var bags = _rules.ToDictionary(r => r.RuleId, _ => new ConcurrentBag<SecurityIssueDto>());

            await Parallel.ForEachAsync(ctx.Documents,
                new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                (doc, token) =>
                {
                    var root  = ctx.GetRoot(doc);
                    var model = ctx.GetModel(doc);
                    if (root is null) return ValueTask.CompletedTask;
                    var filePath = ctx.GetFilePath(doc);
                    foreach (var rule in _rules)
                    foreach (var issue in rule.Analyze(root, model, filePath, token))
                        bags[rule.RuleId].Add(issue);
                    return ValueTask.CompletedTask;
                });

            result.HardcodedCredentials.AddRange(bags["SEC001"]);
            result.SqlInjectionRisks.AddRange(bags["SEC002"]);
            result.InsecureRandomUsage.AddRange(bags["SEC003"]);
            result.MissingCancellationTokens.AddRange(bags["SEC004"]);
            result.AllIssues.AddRange(result.HardcodedCredentials);
            result.AllIssues.AddRange(result.SqlInjectionRisks);
            result.AllIssues.AddRange(result.InsecureRandomUsage);
            result.AllIssues.AddRange(result.MissingCancellationTokens);
            result.TotalIssues    = result.AllIssues.Count;
            result.CriticalCount  = result.AllIssues.Count(i => i.Severity == Severity.Critical);
            result.HighCount      = result.AllIssues.Count(i => i.Severity == Severity.High);
            result.MediumCount    = result.AllIssues.Count(i => i.Severity == Severity.Medium);
            result.LowCount       = result.AllIssues.Count(i => i.Severity == Severity.Low);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security analysis error for {Path}", solutionPath);
            result.Errors.Add($"Security analysis error: {ex.Message}");
        }
        return result;
    }

    private async Task<List<SecurityIssueDto>> RunSingleRuleAsync(
        string solutionPath, IAnalysisRule<SecurityIssueDto> rule, CancellationToken ct)
    {
        var (ws, sol) = await _workspace.LoadSolutionAsync(solutionPath, ct);
        var bag = new ConcurrentBag<SecurityIssueDto>();
        await using var ctx = await AnalysisContext.BuildAsync(sol, ws, _workspace, null, _logger, ct);
        await Parallel.ForEachAsync(ctx.Documents,
            new ParallelOptions { CancellationToken = ct },
            (doc, token) =>
            {
                var root  = ctx.GetRoot(doc);
                var model = ctx.GetModel(doc);
                if (root is null) return ValueTask.CompletedTask;
                foreach (var issue in rule.Analyze(root, model, ctx.GetFilePath(doc), token))
                    bag.Add(issue);
                return ValueTask.CompletedTask;
            });
        return bag.ToList();
    }
}
