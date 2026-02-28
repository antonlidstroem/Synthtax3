using Synthtax.API.Services;
using Synthtax.API.Services.Analysis;
using Synthtax.API.Services.Background;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Extensions;

public static class AnalysisServiceExtensions
{
    public static IServiceCollection AddAnalysisServices(this IServiceCollection services)
    {
        // ── Roslyn analysitjänster ────────────────────────────────────────────
        services.AddScoped<ICodeAnalysisService,       CodeAnalysisService>();
        services.AddScoped<IMetricsService,            MetricsService>();
        services.AddScoped<IMethodExplorerService,     MethodExplorerService>();
        services.AddScoped<ICommentExplorerService,    CommentExplorerService>();
        services.AddScoped<IStructureAnalysisService,  StructureAnalysisService>();
        services.AddScoped<IGitAnalysisService,        GitAnalysisService>();
        services.AddScoped<ISecurityAnalysisService,   SecurityAnalysisService>();
        services.AddScoped<IAIDetectionService,        AIDetectionService>();
        services.AddScoped<ICouplingAnalysisService,   CouplingAnalysisService>();
        services.AddScoped<IRefactoringService,        RefactoringService>();

        // ── Workspace / pipeline ──────────────────────────────────────────────
        services.AddScoped<IRoslynWorkspaceService,    RoslynWorkspaceService>();
        services.AddScoped<ISolutionAnalysisPipeline,  SolutionAnalysisPipeline>();

        // ── Semantiska tjänster (kräver full workspace) ───────────────────────
        services.AddScoped<SemanticCodeAnalysisService>();
        services.AddScoped<SemanticSecurityAnalysisService>();

        // ── Export (singleton – trådsäker, dyr att instansiera) ──────────────
        services.AddSingleton<IExportService, ExportService>();

        // ── Bakgrundstjänst för periodisk SQLite-rensning ─────────────────────
        // Intervall konfigureras via CacheCleanup:IntervalMinutes (default 60 min)
        services.AddHostedService<CacheCleanupBackgroundService>();

        return services;
    }
}
