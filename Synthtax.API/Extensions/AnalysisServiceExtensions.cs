using Synthtax.API.Services;
using Synthtax.API.Services.Analysis;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Extensions;

public static class AnalysisServiceExtensions
{
    public static IServiceCollection AddAnalysisServices(this IServiceCollection services)
    {
        // ── Existing services (unchanged) ────────────────────────────────────
        services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<IMethodExplorerService, MethodExplorerService>();
        services.AddScoped<ICommentExplorerService, CommentExplorerService>();
        services.AddScoped<IStructureAnalysisService, StructureAnalysisService>();
        services.AddScoped<IGitAnalysisService, GitAnalysisService>();
        services.AddScoped<ISecurityAnalysisService, SecurityAnalysisService>();
        services.AddScoped<IAIDetectionService, AIDetectionService>();
        services.AddSingleton<IExportService, ExportService>();

        // ── NEW: Semantic analysis services ──────────────────────────────────
        services.AddScoped<SemanticCodeAnalysisService>();
        services.AddScoped<SemanticSecurityAnalysisService>();

        return services;
    }
}
