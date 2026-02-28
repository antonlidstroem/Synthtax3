using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Plugins;

/// <summary>
/// Discovers all ILanguagePlugin instances registered in the DI container.
/// Adding a new language only requires registering a new ILanguagePlugin – no changes here.
/// </summary>
public sealed class LanguagePluginRegistry : ILanguagePluginRegistry
{
    private readonly Dictionary<string, ILanguagePlugin> _byExt;
    private readonly Dictionary<string, ILanguagePlugin> _byLang;

    public IReadOnlyList<ILanguagePlugin> AllPlugins { get; }

    public LanguagePluginRegistry(IEnumerable<ILanguagePlugin> plugins)
    {
        AllPlugins = plugins.ToList().AsReadOnly();
        _byExt     = new Dictionary<string, ILanguagePlugin>(StringComparer.OrdinalIgnoreCase);
        _byLang    = new Dictionary<string, ILanguagePlugin>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in AllPlugins)
        {
            _byLang[plugin.Language] = plugin;
            foreach (var ext in plugin.SupportedExtensions)
            {
                // Normalise to lowercase with leading dot
                var key = (ext.StartsWith('.') ? ext : "." + ext).ToLowerInvariant();
                _byExt[key] = plugin;
            }
        }
    }

    public ILanguagePlugin? GetByExtension(string extension)
    {
        var key = (extension.StartsWith('.') ? extension : "." + extension).ToLowerInvariant();
        return _byExt.TryGetValue(key, out var p) ? p : null;
    }

    public ILanguagePlugin? GetByLanguage(string language)
        => _byLang.TryGetValue(language, out var p) ? p : null;
}
