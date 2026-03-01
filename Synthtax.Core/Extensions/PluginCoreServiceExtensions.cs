using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Fingerprinting;

namespace Synthtax.Core.Extensions;

// ═══════════════════════════════════════════════════════════════════════════
// DI-registrering
// ═══════════════════════════════════════════════════════════════════════════

public static class PluginCoreServiceExtensions
{
    /// <summary>
    /// Registrerar Fas 2-komponenter: FingerprintService och PluginRegistry.
    ///
    /// <para>Anropas i Program.cs (eller i respektive AddXxx-extension):</para>
    /// <code>
    ///   builder.Services.AddPluginCore();
    ///
    ///   // Registrera sedan plugins:
    ///   builder.Services.AddSingleton&lt;IAnalysisPlugin, CSharpRoslynPlugin&gt;();
    ///   builder.Services.AddSingleton&lt;IAnalysisPlugin, PythonPlugin&gt;();
    /// </code>
    /// </summary>
    public static IServiceCollection AddPluginCore(this IServiceCollection services)
    {
        services.AddSingleton<IFingerprintService, FingerprintService>();
        services.AddSingleton<IPluginRegistry, PluginRegistry>();
        return services;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PluginRegistry — konkret implementering
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Samlar alla <see cref="IAnalysisPlugin"/>-instanser registrerade via DI
/// och exponerar dem via <see cref="IPluginRegistry"/>.
///
/// <para>Singleton — alla plugins är Singleton, registret delas.</para>
/// </summary>
internal sealed class PluginRegistry : IPluginRegistry
{
    private readonly IReadOnlyList<IAnalysisPlugin> _plugins;
    private readonly ILogger<PluginRegistry>         _logger;

    // Intern lookup: filändelse (uppercase) → plugins
    private readonly Dictionary<string, List<IAnalysisPlugin>> _byExtension
        = new(StringComparer.OrdinalIgnoreCase);

    // Intern lookup: pluginId (lowercase) → plugin
    private readonly Dictionary<string, IAnalysisPlugin> _byId
        = new(StringComparer.OrdinalIgnoreCase);

    public PluginRegistry(
        IEnumerable<IAnalysisPlugin> plugins,
        ILogger<PluginRegistry> logger)
    {
        _logger  = logger;
        _plugins = plugins.ToList().AsReadOnly();

        foreach (var plugin in _plugins)
        {
            // ID-index
            if (_byId.TryGetValue(plugin.PluginId, out var existing))
            {
                _logger.LogWarning(
                    "Duplicate PluginId '{Id}': {ExistingPlugin} och {NewPlugin}. " +
                    "Den senast registrerade vinner.",
                    plugin.PluginId, existing.DisplayName, plugin.DisplayName);
            }
            _byId[plugin.PluginId] = plugin;

            // Filändelse-index
            foreach (var ext in plugin.SupportedExtensions)
            {
                if (!_byExtension.TryGetValue(ext, out var list))
                {
                    list = [];
                    _byExtension[ext] = list;
                }
                list.Add(plugin);
            }

            _logger.LogDebug(
                "Plugin registrerat: {Id} v{Version} — {Rules} regler — {Exts}",
                plugin.PluginId, plugin.Version,
                plugin.Rules.Count,
                string.Join(", ", plugin.SupportedExtensions));
        }

        _logger.LogInformation(
            "PluginRegistry initierat: {PluginCount} plugins, {ExtCount} filändelser.",
            _plugins.Count, _byExtension.Count);
    }

    public IReadOnlyList<IAnalysisPlugin> GetAll() => _plugins;

    public IReadOnlyList<IAnalysisPlugin> GetFor(string fileExtension)
    {
        var ext = fileExtension.StartsWith('.') ? fileExtension : $".{fileExtension}";
        return _byExtension.TryGetValue(ext, out var list)
            ? list.AsReadOnly()
            : [];
    }

    public IAnalysisPlugin? GetById(string pluginId) =>
        _byId.TryGetValue(pluginId, out var p) ? p : null;

    public IReadOnlyList<(IAnalysisPlugin Plugin, IPluginRule Rule)> GetAllRules() =>
        _plugins
            .SelectMany(p => p.Rules.Select(r => (Plugin: p, Rule: r)))
            .ToList()
            .AsReadOnly();
}
