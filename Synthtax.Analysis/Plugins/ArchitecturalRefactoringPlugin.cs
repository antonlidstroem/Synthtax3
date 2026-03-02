using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Rules;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Plugins;

/// <summary>
/// IAnalysisPlugin som buntar ihop de tre arkitektoniska refaktoriseringsreglerna
/// i Fas 6:
///
/// <list type="table">
///   <listheader><term>RuleId</term><term>Namn</term></listheader>
///   <item><term>CA006</term><term>NotImplementedException Detector</term></item>
///   <item><term>CA007</term><term>Type Extraction (Multiple Types Per File)</term></item>
///   <item><term>CA008</term><term>Method Extraction (Complex Methods)</term></item>
/// </list>
///
/// <para><b>Registrering i DI:</b>
/// <code>
///   services.AddSingleton&lt;IAnalysisPlugin, ArchitecturalRefactoringPlugin&gt;();
/// </code>
/// </para>
/// </summary>
public sealed class ArchitecturalRefactoringPlugin : AnalysisPluginBase
{
    public override string PluginId    { get; } = "csharp-architectural";
    public override string DisplayName { get; } = "C# Architectural Refactoring Analyzer";
    public override string Version     { get; } = "1.0.0";

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".cs"];

    public override IReadOnlyList<IPluginRule> Rules { get; } =
    [
        new PluginRuleDescriptor
        {
            RuleId          = NotImplementedExceptionRule.RuleId,
            Name            = "NotImplementedException Detector",
            Description     = "Detects throw new NotImplementedException() placeholders and generates starter code.",
            Category        = "Completeness",
            DefaultSeverity = Severity.High,
            IsEnabled       = true,
            DocumentationUri = "https://docs.synthtax.dev/rules/CA006"
        },
        new PluginRuleDescriptor
        {
            RuleId          = TypeExtractionRule.RuleId,
            Name            = "Multiple Types Per File",
            Description     = "Flags files containing more than one top-level type declaration.",
            Category        = "Architecture",
            DefaultSeverity = Severity.Medium,
            IsEnabled       = true,
            DocumentationUri = "https://docs.synthtax.dev/rules/CA007"
        },
        new PluginRuleDescriptor
        {
            RuleId          = MethodExtractionRule.RuleId,
            Name            = "Complex Method — Extraction Candidate",
            Description     = "Identifies methods with high cyclomatic or cognitive complexity that should be split.",
            Category        = "Maintainability",
            DefaultSeverity = Severity.Medium,
            IsEnabled       = true,
            DocumentationUri = "https://docs.synthtax.dev/rules/CA008"
        }
    ];

    public ArchitecturalRefactoringPlugin(
        ILogger<ArchitecturalRefactoringPlugin> logger) : base(logger) { }

    // ═══════════════════════════════════════════════════════════════════════
    // Kärn-analys
    // ═══════════════════════════════════════════════════════════════════════

    protected override async Task<IReadOnlyList<RawIssue>> AnalyzeFileAsync(
        AnalysisRequest request)
    {
        // Parsa C#-filen med Roslyn
        var tree = CSharpSyntaxTree.ParseText(
            request.FileContent,
            path: request.FilePath,
            cancellationToken: request.CancellationToken);

        var root = await tree.GetRootAsync(request.CancellationToken);
        var issues = new List<RawIssue>();

        // ── CA006: NotImplementedException ───────────────────────────────
        if (IsRuleEnabled(request, NotImplementedExceptionRule.RuleId))
        {
            issues.AddRange(NotImplementedExceptionRule.Analyze(
                root, request.FilePath, request.CancellationToken));
        }

        // ── CA007: Type Extraction ────────────────────────────────────────
        if (IsRuleEnabled(request, TypeExtractionRule.RuleId))
        {
            issues.AddRange(TypeExtractionRule.Analyze(
                root, request.FilePath, request.CancellationToken));
        }

        // ── CA008: Method Extraction ──────────────────────────────────────
        if (IsRuleEnabled(request, MethodExtractionRule.RuleId))
        {
            issues.AddRange(MethodExtractionRule.Analyze(
                root, request.FilePath, request.CancellationToken));
        }

        return issues.AsReadOnly();
    }

    // ── Hjälpmetod ────────────────────────────────────────────────────────

    private static bool IsRuleEnabled(AnalysisRequest request, string ruleId) =>
        request.EnabledRuleIds is null || request.EnabledRuleIds.Contains(ruleId);
}
