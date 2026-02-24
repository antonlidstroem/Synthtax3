using Synthtax.API.Services;
using Synthtax.API.Services.Analysis;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Extensions;

public static class AnalysisServiceExtensions
{
    public static IServiceCollection AddAnalysisServices(this IServiceCollection services)
    {
        // ── Roslyn analysis – Scoped (MSBuildWorkspace per request) ──────
        services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<IMethodExplorerService, MethodExplorerService>();
        services.AddScoped<ICommentExplorerService, CommentExplorerService>();
        services.AddScoped<IStructureAnalysisService, StructureAnalysisService>();

        // ── Git – Scoped (LibGit2Sharp Repository per request) ───────────
        services.AddScoped<IGitAnalysisService, GitAnalysisService>();

        // ── Security – Scoped (opens Roslyn workspace) ───────────────────
        services.AddScoped<ISecurityAnalysisService, SecurityAnalysisService>();

        // ── AI Detection – Scoped ────────────────────────────────────────
        services.AddScoped<IAIDetectionService, AIDetectionService>();

        // ── Export – Singleton (no state) ────────────────────────────────
        services.AddSingleton<IExportService, ExportService>();

        return services;
    }
}
