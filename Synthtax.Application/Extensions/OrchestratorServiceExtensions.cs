using Microsoft.Extensions.DependencyInjection;
using Synthtax.Application.Orchestration;
using Synthtax.Core.Orchestration;

namespace Synthtax.Application.Extensions;

public static class OrchestratorServiceExtensions
{
    /// <summary>
    /// Registrerar Fas 3-komponenterna: SyncEngine, SyncWriter, FileSystemScanner
    /// och AnalysisOrchestrator.
    ///
    /// <para>Förutsätter att Fas 1 (<c>AddDomainInfrastructure</c>) och
    /// Fas 2 (<c>AddPluginCore</c>) redan är registrerade.</para>
    ///
    /// <para>Anropas i Program.cs:</para>
    /// <code>
    ///   builder.Services.AddDomainInfrastructure(builder.Configuration);  // Fas 1
    ///   builder.Services.AddPluginCore();                                   // Fas 2
    ///   builder.Services.AddOrchestrator();                                 // Fas 3
    /// </code>
    /// </summary>
    public static IServiceCollection AddOrchestrator(this IServiceCollection services)
    {
        // SyncEngine är stateless — Singleton
        services.AddSingleton<SyncEngine>();

        // SyncWriter beror på DbContext → Scoped
        services.AddScoped<SyncWriter>();

        // FileSystemScanner är stateless — Singleton
        services.AddSingleton<IFileScanner, FileSystemScanner>();

        // AnalysisOrchestrator beror på DbContext (Scoped) → Scoped
        services.AddScoped<IAnalysisOrchestrator, AnalysisOrchestrator>();

        return services;
    }
}
