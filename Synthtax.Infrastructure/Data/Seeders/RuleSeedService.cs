using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Interfaces;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Data.Seeders;

public sealed class RuleSeedService : IHostedService
{
    private readonly IServiceScopeFactory    _scopeFactory;
    private readonly ILanguagePluginRegistry _registry;
    private readonly ILogger<RuleSeedService> _logger;

    public RuleSeedService(
        IServiceScopeFactory     scopeFactory,
        ILanguagePluginRegistry  registry,
        ILogger<RuleSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry     = registry;
        _logger       = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("RuleSeedService: syncing plugin rules with database…");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SynthtaxDbContext>();

        var pluginRules = _registry.GetAllRules()
            .ToDictionary(r => r.RuleId, StringComparer.OrdinalIgnoreCase);

        var dbRules = await db.Rules
            .IgnoreQueryFilters()
            .ToDictionaryAsync(r => r.RuleId, StringComparer.OrdinalIgnoreCase, ct);

        int inserted = 0, updated = 0, deactivated = 0;

        foreach (var (ruleId, pluginRule) in pluginRules)
        {
            if (dbRules.TryGetValue(ruleId, out var existing))
            {
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

        foreach (var (ruleId, dbRule) in dbRules)
        {
            if (!pluginRules.ContainsKey(ruleId) && dbRule.IsEnabled)
            {
                dbRule.IsEnabled = false;
                deactivated++;
                _logger.LogWarning("Rule '{RuleId}' no longer exists in any plugin — deactivated.", ruleId);
            }
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "RuleSeedService done: {Inserted} added, {Updated} updated, {Deactivated} deactivated.",
            inserted, updated, deactivated);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// ─── Extension helpers ────────────────────────────────────────────────────

internal static class PluginRegistryExtensions
{
    /// <summary>Flat list of all rules across all registered plugins.</summary>
    public static IEnumerable<RuleSeedDto> GetAllRules(this ILanguagePluginRegistry registry) =>
        registry.GetAllPlugins()          // ← ILanguagePluginRegistry must expose GetAllPlugins()
                .SelectMany(plugin => plugin.Rules.Select(rule => new RuleSeedDto(
                    RuleId:          rule.RuleId,
                    Name:            rule.Name,
                    Description:     rule.Description,
                    Category:        plugin.Language,
                    DefaultSeverity: rule.DefaultSeverity,
                    Version:         plugin.Version)));
}

internal sealed record RuleSeedDto(
    string   RuleId,
    string   Name,
    string   Description,
    string   Category,
    Severity DefaultSeverity,
    string   Version);
