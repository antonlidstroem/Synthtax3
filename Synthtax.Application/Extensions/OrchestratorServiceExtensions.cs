using Microsoft.Extensions.DependencyInjection;
using Synthtax.Application.Orchestration;
using Synthtax.Core.Orchestration;
using Synthtax.Domain.Entities;

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

    public async Task SyncAsync(AnalysisSession session, ...)
    {
        var (added, removed) = await _writer.CommitDiffAsync(...);

        // ── Fas 8: Pusha realtidsuppdatering ─────────────────────────────────
        await _hubPusher.PushAnalysisUpdatedAsync(new AnalysisUpdatedPayload
        {
            OrganizationId = _currentUser.OrganizationId ?? Guid.Empty,
            ProjectId = session.ProjectId,
            ProjectName = project.Name,
            SessionId = session.Id,
            CompletedAt = DateTime.UtcNow,
            NewIssuesCount = added.Count,
            ResolvedIssuesCount = removed.Count,
            TotalOpenIssues = await CountOpenIssuesAsync(session.ProjectId),
            HealthScore = CalculateHealthScore(session),
            IsCiCdTriggered = session.IsCiCdTriggered,
            NewIssues = added.Take(50).Select(MapToSummary).ToList(),
            ResolvedFingerprints = removed.Select(i => i.Fingerprint).ToList()
        });
    }
}
