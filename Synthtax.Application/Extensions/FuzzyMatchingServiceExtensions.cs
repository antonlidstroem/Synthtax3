using Microsoft.Extensions.DependencyInjection;
using Synthtax.Application.Orchestration;
using Synthtax.Core.FuzzyMatching;
using Synthtax.Core.Tokenization;

namespace Synthtax.Application.Extensions;

public static class FuzzyMatchingServiceExtensions
{
    /// <summary>
    /// Registrerar Fas 4-komponenterna: tokenizer, MinHash-index, FuzzyMatchService
    /// och FuzzyAwareSyncEngineV2/Writer.
    ///
    /// <para>Förutsätter att Fas 1–3 är registrerade (AddDomainInfrastructure,
    /// AddPluginCore, AddOrchestrator).</para>
    ///
    /// <code>
    ///   builder.Services.AddFuzzyMatching();
    ///   // Valfritt: konfigurera threshold
    ///   builder.Services.Configure&lt;FuzzyMatchOptions&gt;(o => o.Threshold = 0.88);
    /// </code>
    /// </summary>
    public static IServiceCollection AddFuzzyMatching(this IServiceCollection services)
    {
        // StructuralTokenizer är stateless → Singleton
        services.AddSingleton<StructuralTokenizer>();

        // FuzzyMatchService är thread-safe → Singleton
        services.AddSingleton<IFuzzyMatchService, FuzzyMatchService>();

        // FuzzyAwareSyncEngineV2 beror på SyncEngine (Singleton) + FP-service (Singleton)
        // → Singleton
        services.AddSingleton<FuzzyAwareSyncEngineV2>();

        // FuzzyAwareSyncWriter beror på SyncWriter (Scoped) + DbContext (Scoped)
        // → Scoped
        services.AddScoped<FuzzyAwareSyncWriter>();

        return services;
    }
}
