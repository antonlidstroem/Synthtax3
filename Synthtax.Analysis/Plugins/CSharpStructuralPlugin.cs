using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Rules;
using Synthtax.Core.Contracts;

namespace Synthtax.Analysis.Plugins;

/// <summary>
/// Fas 6-plugin som kör strukturella regler på C#-kod:
/// <list type="bullet">
///   <item><see cref="NotImplementedExceptionRule"/> (SA001) — stub-detektion.</item>
///   <item><see cref="MultiClassFileRule"/> (SA002) — typextraktionskandidater.</item>
///   <item><see cref="ComplexMethodRule"/> (SA003) — metodextraktionskandidater.</item>
/// </list>
///
/// <para>Implementerar <see cref="IAnalysisPlugin"/> från Fas 2 —
/// kan registreras direkt i <c>IPluginRegistry</c> via <c>AddSingleton&lt;IAnalysisPlugin, CSharpStructuralPlugin&gt;()</c>.</para>
/// </summary>
public sealed class CSharpStructuralPlugin : AnalysisPluginBase
{
    private readonly NotImplementedExceptionRule _sa001;
    private readonly MultiClassFileRule          _sa002;
    private readonly ComplexMethodRule           _sa003;

    public CSharpStructuralPlugin(ILogger<CSharpStructuralPlugin> logger)
        : base(logger)
    {
        _sa001 = new NotImplementedExceptionRule();
        _sa002 = new MultiClassFileRule();
        _sa003 = new ComplexMethodRule();
    }

    // ── IAnalysisPlugin ────────────────────────────────────────────────────

    public override string PluginId            => "synthtax-csharp-structural";
    public override string DisplayName         => "C# Structural Analysis (Fas 6)";
    public override string Version             => "6.0.0";
    public override IReadOnlyList<string> SupportedExtensions => [".cs"];

    public override IReadOnlyList<IPluginRule> Rules =>
    [
        new PluginRuleDescriptor
        {
            RuleId          = NotImplementedExceptionRule.RuleId,
            Name            = "NotImplementedException Detected",
            Description     = "A method or property getter throws NotImplementedException, " +
                              "indicating it is an unfinished stub. " +
                              "The PromptFactory generates starter code for AI-assisted implementation.",
            Category        = "Implementation",
            DefaultSeverity = Synthtax.Core.Enums.Severity.High,
            IsEnabled        = true,
            DocumentationUri = new Uri("https://docs.synthtax.dev/rules/SA001")
        },
        new PluginRuleDescriptor
        {
            RuleId          = MultiClassFileRule.RuleId,
            Name            = "Multiple Type Declarations in Single File",
            Description     = "The file contains more than one top-level type declaration. " +
                              "Each class, interface, record, enum, or delegate should reside " +
                              "in its own file to improve navigability and adhere to SRP.",
            Category        = "Structure",
            DefaultSeverity = Synthtax.Core.Enums.Severity.Medium,
            IsEnabled        = true,
            DocumentationUri = new Uri("https://docs.synthtax.dev/rules/SA002")
        },
        new PluginRuleDescriptor
        {
            RuleId          = ComplexMethodRule.RuleId,
            Name            = "Complex Method — Extraction Candidate",
            Description     = "A method exceeds the cyclomatic complexity threshold (≥10), " +
                              "line count threshold (≥30 lines), or nesting depth threshold (≥4). " +
                              "The PromptFactory generates a refactoring spec with extraction hints.",
            Category        = "Maintainability",
            DefaultSeverity = Synthtax.Core.Enums.Severity.Medium,
            IsEnabled        = true,
            DocumentationUri = new Uri("https://docs.synthtax.dev/rules/SA003")
        }
    ];

    // ── Analyslogik ────────────────────────────────────────────────────────

    protected override Task<IReadOnlyList<RawIssue>> AnalyzeFileAsync(AnalysisRequest request)
    {
        // Parse till SyntaxTree
        var tree = CSharpSyntaxTree.ParseText(
            request.FileContent,
            path: request.FilePath,
            cancellationToken: request.CancellationToken);

        // Kör alla aktiva regler
        var all    = new List<RawIssue>();
        var active = request.EnabledRuleIds;

        all.AddRange(_sa001.Analyze(tree, model: null, request.FilePath, active));
        all.AddRange(_sa002.Analyze(tree, request.FilePath, active));
        all.AddRange(_sa003.Analyze(tree, request.FilePath, active));

        return Task.FromResult<IReadOnlyList<RawIssue>>(all.AsReadOnly());
    }
}
