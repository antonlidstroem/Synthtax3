using Microsoft.Extensions.DependencyInjection;
using Synthtax.API.Services;
using Synthtax.API.Services.Analysis;
using Synthtax.API.Services.Analysis.Rules;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Extensions;

public static class AnalysisServiceExtensions
{
    public static IServiceCollection AddAnalysisServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // ── Workspace (replaces static RoslynWorkspaceHelper) ─────────────────
        services.Configure<WorkspaceOptions>(opts =>
        {
            // Exclusions can be driven from configuration if needed
            // configuration?.GetSection("Analysis:Workspace").Bind(opts);
        });
        services.AddScoped<IRoslynWorkspaceService, RoslynWorkspaceService>();

        // ── Code analysis rules (scoped; can be replaced per tenant/request) ──
        services.AddScoped<IAnalysisRule<CodeIssueDto>, LongMethodRule>();
        services.AddScoped<IAnalysisRule<CodeIssueDto>, DeadVariableRule>();
        services.AddScoped<IAnalysisRule<CodeIssueDto>, UnnecessaryUsingRule>();

        // ── Security analysis rules ───────────────────────────────────────────
        services.AddScoped<IAnalysisRule<SecurityIssueDto>, HardcodedCredentialRule>();
        services.AddScoped<IAnalysisRule<SecurityIssueDto>, SqlInjectionRule>();
        services.AddScoped<IAnalysisRule<SecurityIssueDto>, InsecureRandomRule>();
        services.AddScoped<IAnalysisRule<SecurityIssueDto>, MissingCancellationTokenRule>();

        // ── Analysis services ─────────────────────────────────────────────────
        services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<IMethodExplorerService, MethodExplorerService>();
        services.AddScoped<ICommentExplorerService, CommentExplorerService>();
        services.AddScoped<IStructureAnalysisService, StructureAnalysisService>();
        services.AddScoped<IGitAnalysisService, GitAnalysisService>();
        services.AddScoped<ISecurityAnalysisService, SecurityAnalysisService>();
        services.AddScoped<IAIDetectionService, AIDetectionService>();

        // ── NEW services ──────────────────────────────────────────────────────
        services.AddScoped<ICouplingAnalysisService, CouplingAnalysisService>();
        services.AddScoped<IRefactoringService, RefactoringService>();
        services.AddScoped<ISolutionAnalysisPipeline, SolutionAnalysisPipeline>();

        // Export is singleton because QuestPDF.Settings is static
        services.AddSingleton<IExportService, ExportService>();

        return services;
    }
}
