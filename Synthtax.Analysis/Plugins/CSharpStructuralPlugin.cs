using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Rules;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Plugins;

/// <summary>
/// Structural analysis plugin that runs SA001/SA002/SA003 on C# files.
/// </summary>
public sealed class CSharpStructuralPlugin : AnalysisPluginBase
{
    private readonly NotImplementedExceptionRule _sa001 = new();
    private readonly MultiClassFileRule          _sa002 = new();
    private readonly ComplexMethodRule           _sa003 = new();

    public override string PluginId            => "synthtax-csharp-structural";
    public override string DisplayName         => "C# Structural Analysis";
    public override string Version             => "6.0.0";
    public override IReadOnlyList<string> SupportedExtensions => [".cs"];

    public CSharpStructuralPlugin(ILogger<CSharpStructuralPlugin> logger) : base(logger) { }

    public override IReadOnlyList<IPluginRule> Rules =>
    [
        new PluginRuleDescriptor
        {
            RuleId           = NotImplementedExceptionRule.RuleId,
            Name             = "NotImplementedException Detected",
            Description      = "Unfinished stub — throws NotImplementedException.",
            Category         = "Implementation",
            DefaultSeverity  = Severity.High,
            IsEnabled        = true,
            DocumentationUri = new Uri("https://docs.synthtax.dev/rules/SA001")
        },
        new PluginRuleDescriptor
        {
            RuleId           = MultiClassFileRule.RuleId,
            Name             = "Multiple Type Declarations in Single File",
            Description      = "File contains more than one top-level type declaration.",
            Category         = "Structure",
            DefaultSeverity  = Severity.Medium,
            IsEnabled        = true,
            DocumentationUri = new Uri("https://docs.synthtax.dev/rules/SA002")
        },
        new PluginRuleDescriptor
        {
            RuleId           = ComplexMethodRule.RuleId,
            Name             = "Complex Method — Extraction Candidate",
            Description      = "Method exceeds complexity or length threshold.",
            Category         = "Maintainability",
            DefaultSeverity  = Severity.Medium,
            IsEnabled        = true,
            DocumentationUri = new Uri("https://docs.synthtax.dev/rules/SA003")
        }
    ];

    protected override Task<IReadOnlyList<RawIssue>> AnalyzeFileAsync(AnalysisRequest request)
    {
        var tree   = CSharpSyntaxTree.ParseText(
            request.FileContent,
            path: request.FilePath,
            cancellationToken: request.CancellationToken);

        var active = request.EnabledRuleIds;
        var all    = new List<RawIssue>();

        all.AddRange(_sa001.Analyze(tree, request.FilePath, active));
        all.AddRange(_sa002.Analyze(tree, request.FilePath, active));
        all.AddRange(_sa003.Analyze(tree, request.FilePath, active));

        return Task.FromResult<IReadOnlyList<RawIssue>>(all.AsReadOnly());
    }
}
