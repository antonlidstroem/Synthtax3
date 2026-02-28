using Microsoft.Extensions.DependencyInjection;
using Synthtax.Analysis.Languages.Css;
using Synthtax.Analysis.Languages.Html;
using Synthtax.Analysis.Languages.JavaScript;
using Synthtax.Analysis.Plugins;
using Synthtax.Analysis.Services;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis;

public static class AnalysisServiceExtensions   // extends the existing class in Synthtax.Analysis
{
    /// <summary>
    /// Registers the web-language plugin system and commit message service.
    /// Call from the existing AddSynthtaxAnalysis() extension, or independently.
    /// </summary>
    public static IServiceCollection AddWebLanguageAnalysis(this IServiceCollection services)
    {
        // ── Built-in language plugins ─────────────────────────────────────────
        // New language? Register another ILanguagePlugin here.
        services.AddSingleton<ILanguagePlugin, CssPlugin>();
        services.AddSingleton<ILanguagePlugin, JavaScriptPlugin>();
        services.AddSingleton<ILanguagePlugin, HtmlPlugin>();

        // ── Plugin registry & service ─────────────────────────────────────────
        services.AddSingleton<ILanguagePluginRegistry, LanguagePluginRegistry>();
        services.AddScoped<IWebLanguageAnalysisService, WebLanguageAnalysisService>();

        // ── Commit message suggestion ─────────────────────────────────────────
        services.AddScoped<ICommitMessageService, CommitMessageService>();

        return services;
    }
}
