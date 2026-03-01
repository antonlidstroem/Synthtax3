using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Interfaces;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Data.Seeders;

/// <summary>
/// Starttjänst som synkroniserar plugin-regelregistret med Rule-tabellen.
///
/// <para>Körs en gång vid uppstart (IHostedService). Logiken:
/// <list type="number">
///   <item>Läs alla regler från <see cref="ILanguagePluginRegistry"/>.</item>
///   <item>Upserta varje regel i databasen (INSERT om ny, UPDATE om version ändrats).</item>
///   <item>Regler som inte längre finns i något plugin markeras som IsEnabled = false.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RuleSeedService : IHostedService
{
    private readonly IServiceScopeFactory   _scopeFactory;
    private readonly ILanguagePluginRegistry _registry;
    private readonly ILogger<RuleSeedService> _logger;

    public RuleSeedService(
        IServiceScopeFactory    scopeFactory,
        ILanguagePluginRegistry registry,
        ILogger<RuleSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry     = registry;
        _logger       = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("RuleSeedService: synkroniserar plugin-regler med databasen...");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SynthtaxDbContext>();

        // Hämta alla regler från plugin-systemet
        var pluginRules = _registry.GetAllRules()
            .ToDictionary(r => r.RuleId, StringComparer.OrdinalIgnoreCase);

        // Hämta befintliga regler från DB (IgnoreQueryFilters för att nå inaktiverade)
        var dbRules = await db.Rules
            .IgnoreQueryFilters()
            .ToDictionaryAsync(r => r.RuleId, StringComparer.OrdinalIgnoreCase, ct);

        int inserted = 0, updated = 0, deactivated = 0;

        // Upserta plugin-regler → databas
        foreach (var (ruleId, pluginRule) in pluginRules)
        {
            if (dbRules.TryGetValue(ruleId, out var existing))
            {
                // Uppdatera om version eller metadata har ändrats
                if (existing.Version != pluginRule.Version ||
                    existing.Description != pluginRule.Description ||
                    !existing.IsEnabled)
                {
                    existing.Name            = pluginRule.Name;
                    existing.Description     = pluginRule.Description;
                    existing.Category        = pluginRule.Category;
                    existing.DefaultSeverity = pluginRule.DefaultSeverity;
                    existing.Version         = pluginRule.Version;
                    existing.IsEnabled       = true;
                    updated++;
                }
            }
            else
            {
                db.Rules.Add(new Rule
                {
                    RuleId          = ruleId,
                    Name            = pluginRule.Name,
                    Description     = pluginRule.Description,
                    Category        = pluginRule.Category,
                    DefaultSeverity = pluginRule.DefaultSeverity,
                    Version         = pluginRule.Version,
                    IsEnabled       = true
                });
                inserted++;
            }
        }

        // Avaktivera regler vars plugin inte längre finns
        foreach (var (ruleId, dbRule) in dbRules)
        {
            if (!pluginRules.ContainsKey(ruleId) && dbRule.IsEnabled)
            {
                dbRule.IsEnabled = false;
                deactivated++;
                _logger.LogWarning(
                    "RuleSeedService: Regel '{RuleId}' finns inte längre i något plugin — avaktiverad.",
                    ruleId);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "RuleSeedService klar: {Inserted} tillagda, {Updated} uppdaterade, {Deactivated} avaktiverade.",
            inserted, updated, deactivated);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// ─── Extension på ILanguagePluginRegistry ─────────────────────────────────

/// <summary>
/// Hjälpklass som aggregerar alla regler från alla registrerade plugins.
/// </summary>
internal static class PluginRegistryExtensions
{
    /// <summary>Flat lista av alla regler från samtliga plugins, med plugin-metadata.</summary>
    public static IEnumerable<RuleSeedDto> GetAllRules(this ILanguagePluginRegistry registry) =>
        registry.GetAllPlugins()
                .SelectMany(plugin => plugin.Rules.Select(rule => new RuleSeedDto(
                    RuleId:          rule.RuleId,
                    Name:            rule.Name,
                    Description:     rule.Description,
                    Category:        plugin.Language,
                    DefaultSeverity: rule.DefaultSeverity,
                    Version:         plugin.Version)));
}

internal sealed record RuleSeedDto(
    string                    RuleId,
    string                    Name,
    string                    Description,
    string                    Category,
    Synthtax.Core.Enums.Severity DefaultSeverity,
    string                    Version);
