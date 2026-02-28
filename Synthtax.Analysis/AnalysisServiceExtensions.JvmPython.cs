using Microsoft.Extensions.DependencyInjection;
using Synthtax.Analysis.Languages.Java;
using Synthtax.Analysis.Languages.Python;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis;

/// <summary>
/// Registers Java and Python language plugins.
/// Call after <c>AddWebLanguageAnalysis()</c> in Program.cs.
/// </summary>
public static class JvmPythonServiceExtensions
{
    /// <summary>
    /// Adds Java (JAVA001–JAVA012) and Python (PY001–PY012) analysis plugins
    /// to the existing <see cref="ILanguagePluginRegistry"/> discovery.
    /// </summary>
    public static IServiceCollection AddJvmPythonLanguages(
        this IServiceCollection services)
    {
        services.AddSingleton<ILanguagePlugin, JavaPlugin>();
        services.AddSingleton<ILanguagePlugin, PythonPlugin>();
        return services;
    }
}
