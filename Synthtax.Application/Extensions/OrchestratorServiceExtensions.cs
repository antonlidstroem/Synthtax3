using Microsoft.Extensions.DependencyInjection;
using Synthtax.Application.Orchestration;
using Synthtax.Core.Orchestration;
using Synthtax.Domain.Entities;

namespace Synthtax.Application.Extensions;

public static class OrchestratorServiceExtensions
{
    public static IServiceCollection AddOrchestrator(this IServiceCollection services)
    {
        // SyncEngine är stateless — Singleton
        services.AddSingleton<SyncEngine>();

        // SyncWriter beror på DbContext (via Repository) → Scoped
        services.AddScoped<SyncWriter>();

        // FileSystemScanner är stateless — Singleton
        services.AddSingleton<IFileScanner, FileSystemScanner>();

        // AnalysisOrchestrator hanterar arbetsflödet → Scoped
        services.AddScoped<IAnalysisOrchestrator, AnalysisOrchestrator>();

        return services;
    }

    // OBS: Metoden SyncAsync har tagits bort härifrån. 
    // Den ska ligga inuti klassen AnalysisOrchestrator, 
    // där fält som _writer och _hubPusher är tillgängliga via dependency injection.
}