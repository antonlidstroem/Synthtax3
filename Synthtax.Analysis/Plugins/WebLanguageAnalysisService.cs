using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Plugins;

public class WebLanguageAnalysisService : IWebLanguageAnalysisService
{
    private readonly ILanguagePluginRegistry _registry;
    private readonly ILogger<WebLanguageAnalysisService> _logger;

    public WebLanguageAnalysisService(
        ILanguagePluginRegistry registry,
        ILogger<WebLanguageAnalysisService> logger)
    {
        _registry = registry;
        _logger   = logger;
    }

    public async Task<WebAnalysisResultDto> AnalyzeDirectoryAsync(
        string directoryPath, bool recursive = true, CancellationToken ct = default)
    {
        var result = new WebAnalysisResultDto { ProjectPath = directoryPath };

        if (!Directory.Exists(directoryPath))
        {
            result.Errors.Add($"Directory not found: {directoryPath}");
            return result;
        }

        var bag = new ConcurrentBag<WebFileResultDto>();

        await Parallel.ForEachAsync(
            _registry.AllPlugins,
            new ParallelOptions { CancellationToken = ct },
            async (plugin, token) =>
            {
                try
                {
                    var fileResults = await plugin.AnalyzeDirectoryAsync(directoryPath, recursive, token);
                    foreach (var r in fileResults) bag.Add(r);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Plugin {Lang} failed for {Dir}", plugin.Language, directoryPath);
                    result.Errors.Add($"{plugin.Language}: {ex.Message}");
                }
            });

        result.FileResults.AddRange(bag.OrderBy(r => r.FilePath));
        result.FilesAnalyzed = result.FileResults.Count;

        var all = result.FileResults.SelectMany(r => r.Issues).ToList();
        result.TotalIssues   = all.Count;
        result.CriticalCount = all.Count(i => i.Severity == Severity.Critical);
        result.HighCount     = all.Count(i => i.Severity == Severity.High);
        result.MediumCount   = all.Count(i => i.Severity == Severity.Medium);
        result.LowCount      = all.Count(i => i.Severity == Severity.Low);

        foreach (var g in all.GroupBy(i => i.Language))
            result.ByLanguage[g.Key] = g.ToList();

        return result;
    }

    public async Task<WebFileResultDto> AnalyzeFileAsync(
        string filePath, CancellationToken ct = default)
    {
        var ext    = Path.GetExtension(filePath).ToLowerInvariant();
        var plugin = _registry.GetByExtension(ext);

        if (plugin is null)
            return new WebFileResultDto
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Language = "Unknown",
                Errors   = [$"No plugin registered for extension '{ext}'."]
            };

        return await plugin.AnalyzeFileAsync(filePath, ct);
    }

    public List<LanguagePluginInfoDto> GetRegisteredPlugins()
        => _registry.AllPlugins.Select(p => new LanguagePluginInfoDto
        {
            Language            = p.Language,
            Version             = p.Version,
            SupportedExtensions = p.SupportedExtensions.ToList(),
            Rules               = p.Rules.Select(r => new PluginRuleInfoDto
            {
                RuleId          = r.RuleId,
                Name            = r.Name,
                Description     = r.Description,
                DefaultSeverity = r.DefaultSeverity,
                IsEnabled       = r.IsEnabled
            }).ToList()
        }).ToList();
}
