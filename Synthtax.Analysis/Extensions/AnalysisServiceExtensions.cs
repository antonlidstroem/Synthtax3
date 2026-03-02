using Microsoft.Extensions.DependencyInjection;
using Synthtax.Analysis.Pipeline;
using Synthtax.Analysis.Rules;
using Synthtax.Analysis.Services;
using Synthtax.Analysis.Workspace;
using Synthtax.Analysis.Plugins;
using Synthtax.Analysis.Languages.Css;
using Synthtax.Analysis.Languages.Html;
using Synthtax.Analysis.Languages.JavaScript;
using Synthtax.Analysis.Languages.Java;
using Synthtax.Analysis.Languages.Python;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Extensions;

public static class AnalysisServiceExtensions
{
    public static IServiceCollection AddAnalysisEngine(
        this IServiceCollection services,
        Action<WorkspaceOptions>? configureWorkspace = null)
    {
        // ── 1. Workspace & Pipeline ──────────────────────────────────────────
        if (configureWorkspace is not null)
            services.Configure<WorkspaceOptions>(configureWorkspace);

        services.AddSingleton<IRoslynWorkspaceService, RoslynWorkspaceService>();
        services.AddScoped<ISolutionAnalysisPipeline, SolutionAnalysisPipeline>();

        // ── 2. Core Analysis Services ────────────────────────────────────────
        services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
        services.AddScoped<ISecurityAnalysisService, SecurityAnalysisService>();
        services.AddScoped<IMetricsService, MetricsService>();
        services.AddScoped<ICouplingAnalysisService, CouplingAnalysisService>();
        services.AddScoped<IAIDetectionService, AIDetectionService>();
        services.AddScoped<IRefactoringService, RefactoringService>();
        services.AddScoped<IStructureAnalysisService, StructureAnalysisService>();
        services.AddScoped<IMethodExplorerService, MethodExplorerService>();
        services.AddScoped<ICommentExplorerService, CommentExplorerService>();
        services.AddScoped<IGitAnalysisService, GitAnalysisService>();
        services.AddScoped<ICommitMessageService, CommitMessageService>();

        // Semantiska tjänster
        services.AddScoped<SemanticCodeAnalysisService>();
        services.AddScoped<SemanticSecurityAnalysisService>();

        // ── 3. Språk-Plugins (Web, JVM, Python) ──────────────────────────────
        services.AddSingleton<ILanguagePluginRegistry, LanguagePluginRegistry>();
        services.AddScoped<IWebLanguageAnalysisService, WebLanguageAnalysisService>();

        // Registrera alla språk-plugins som Singletons
        services.AddSingleton<ILanguagePlugin, CssPlugin>();
        services.AddSingleton<ILanguagePlugin, JavaScriptPlugin>();
        services.AddSingleton<ILanguagePlugin, HtmlPlugin>();
        services.AddSingleton<ILanguagePlugin, JavaPlugin>();
        services.AddSingleton<ILanguagePlugin, PythonPlugin>();

        // ── 4. Analysregler (Roslyn) ──────────────────────────────────────────
        // Code Issues
        services.AddTransient<IAnalysisRule<CodeIssueDto>, LongMethodRule>();
        services.AddTransient<IAnalysisRule<CodeIssueDto>, DeadVariableRule>();
        services.AddTransient<IAnalysisRule<CodeIssueDto>, UnnecessaryUsingRule>();

        // Security Issues
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, HardcodedCredentialRule>();
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, SqlInjectionRule>();
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, InsecureRandomRule>();
        services.AddTransient<IAnalysisRule<SecurityIssueDto>, MissingCancellationTokenRule>();

        return services;
    }
}