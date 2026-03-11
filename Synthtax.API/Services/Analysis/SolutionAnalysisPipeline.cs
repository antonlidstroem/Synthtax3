using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
namespace Synthtax.API.Services.Analysis;

public sealed class SolutionAnalysisPipeline : ISolutionAnalysisPipeline
{
    private readonly IRoslynWorkspaceService _workspace;
    private readonly ICodeAnalysisService _code;
    private readonly ISecurityAnalysisService _security;
    private readonly IMetricsService _metrics;
    private readonly ICouplingAnalysisService _coupling;
    private readonly IAIDetectionService _ai;
    private readonly ILogger<SolutionAnalysisPipeline> _logger;
    public SolutionAnalysisPipeline(
        IRoslynWorkspaceService workspace,
        ICodeAnalysisService code,
        ISecurityAnalysisService security,
        IMetricsService metrics,
        ICouplingAnalysisService coupling,
        IAIDetectionService ai,
        ILogger<SolutionAnalysisPipeline> logger)
    {
        _workspace = workspace;
        _code = code;
        _security = security;
        _metrics = metrics;
        _coupling = coupling;
        _ai = ai;
        _logger = logger;
    }
    public async Task<FullAnalysisResultDto> RunFullAnalysisAsync(
        string solutionPath,
        FullAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FullAnalysisOptions();
        var sw = Stopwatch.StartNew();
        var result = new FullAnalysisResultDto { SolutionPath = solutionPath };
        _logger.LogInformation("Pipeline: loading solution {Path}", solutionPath);
        var (workspace, solution) = await _workspace.LoadSolutionAsync(
            solutionPath, cancellationToken);
        await using var ctx = await AnalysisContext.BuildAsync(
            solution, workspace, _workspace, options, _logger, cancellationToken);
        _logger.LogInformation("Pipeline: solution loaded — dispatching analyses.");
        var tasks = new List<Task>();
        if (options.IncludeCode)
            tasks.Add(RunSafe(
                async () => (_code as IContextAwareAnalysis) is { } ca
                    ? (object)await ca.AnalyzeAsync(ctx, cancellationToken)
                    : await _code.AnalyzeSolutionAsync(solutionPath, cancellationToken),
                r => result.Code = r as CodeAnalysisResultDto,
                "Code", result.Errors));
        if (options.IncludeSecurity)
            tasks.Add(RunSafe(
                async () => (_security as IContextAwareAnalysis) is { } ca
                    ? (object)await ca.AnalyzeAsync(ctx, cancellationToken)
                    : await _security.AnalyzeSolutionAsync(solutionPath, cancellationToken),
                r => result.Security = r as SecurityAnalysisResultDto,
                "Security", result.Errors));
        if (options.IncludeMetrics)
            tasks.Add(RunSafe(
                async () => (_metrics as IContextAwareAnalysis) is { } ca
                    ? (object)await ca.AnalyzeAsync(ctx, cancellationToken)
                    : await _metrics.AnalyzeSolutionMetricsAsync(solutionPath, cancellationToken),
                r => result.Metrics = r as MetricsResultDto,
                "Metrics", result.Errors));
        if (options.IncludeCoupling)
            tasks.Add(RunSafe(
                async () => (_coupling as IContextAwareAnalysis) is { } ca
                    ? (object)await ca.AnalyzeAsync(ctx, cancellationToken)
                    : await _coupling.AnalyzeSolutionAsync(solutionPath, cancellationToken),
                r => result.Coupling = r as CouplingAnalysisResultDto,
                "Coupling", result.Errors));
        if (options.IncludeAIDetection)
            tasks.Add(RunSafe(
                async () => (_ai as IContextAwareAnalysis) is { } ca
                    ? (object)await ca.AnalyzeAsync(ctx, cancellationToken)
                    : await _ai.AnalyzeSolutionAsync(solutionPath, cancellationToken),
                r => result.AIDetection = r as AIDetectionResultDto,
                "AIDetection", result.Errors));
        await Task.WhenAll(tasks);
        sw.Stop();
        result.Duration = sw.Elapsed;
        _logger.LogInformation("Pipeline: completed in {Ms} ms.", sw.ElapsedMilliseconds);
        return result;
    }
    private async Task RunSafe(
        Func<Task<object>> factory,
        Action<object?> assign,
        string name,
        IList<string> errors)
    {
        try
        {
            assign(await factory());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline: {Name} analysis failed.", name);
            errors.Add($"{name}: {ex.Message}");
        }
    }
}
public interface IContextAwareAnalysis
{
    Task<object> AnalyzeAsync(AnalysisContext ctx, CancellationToken ct);
}
