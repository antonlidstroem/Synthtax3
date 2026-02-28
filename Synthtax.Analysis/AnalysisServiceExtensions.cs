using Microsoft.Extensions.DependencyInjection;
using Synthtax.Analysis.Pipeline;
using Synthtax.Analysis.Rules;
using Synthtax.Analysis.Services;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis;

public static class AnalysisServiceExtensions
{
    /// <summary>
    /// Registers all Synthtax.Analysis services.
    /// Call this from your host's ConfigureServices (API, WPF, etc.).
    /// </summary>
    public static IServiceCollection AddSynthtaxAnalysis(
        this IServiceCollection services,
        Action<WorkspaceOptions>? configureWorkspace = null)
    {
        // Workspace
        if (configureWorkspace is not null)
            services.Configure<WorkspaceOptions>(configureWorkspace);

        services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>();

        // Analysis rules (registered so services can receive IEnumerable<IAnalysisRule<T>>)
        services.AddTransient<IAnalysisRule<CodeIssueDto>, LongMethodRule>();
        services.AddTransient<IAnalysisRule<CodeIssueDto>, DeadVariableRule>();
        services.AddTransient<IAnalysisRule<CodeIssueDto>, UnnecessaryUsingRule>();

        services.AddTransient<IAnalysisRule<SecurityIssueDto>, HardcodedCredentialRule>();
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, SqlInjectionRule>();
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, InsecureRandomRule>();
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, MissingCancellationTokenRule>();

        // Core analysis services
        services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
        services.AddScoped<ISecurityAnalysisService, SecurityAnalysisService>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<ICouplingAnalysisService, CouplingAnalysisService>();
        services.AddScoped<IAIDetectionService, AIDetectionService>();
        services.AddScoped<IRefactoringService, RefactoringService>();
        services.AddScoped<IStructureAnalysisService, StructureAnalysisService>();
        services.AddScoped<IMethodExplorerService, MethodExplorerService>();
        services.AddScoped<ICommentExplorerService, CommentExplorerService>();
        services.AddScoped<IGitAnalysisService, GitAnalysisService>();

        // Semantic analysis (stateless, no cache dependency in this library)
        services.AddScoped<SemanticCodeAnalysisService>();
        services.AddScoped<SemanticSecurityAnalysisService>();

        // Pipeline
        services.AddScoped<ISolutionAnalysisPipeline, SolutionAnalysisPipeline>();

        return services;
    }
}
