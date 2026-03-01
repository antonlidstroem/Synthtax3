// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  SKISS / REFERENSIMPLEMENTATION                                         ║
// ║  Visar hur befintliga Roslyn-regler adapteras till IAnalysisPlugin.      ║
// ║  Lägg i: Synthtax.Analysis/Plugins/CSharpRoslynPlugin.cs                ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Plugins;

/// <summary>
/// Adapter som kopplar Roslyn-baserade regler till det nya <see cref="IAnalysisPlugin"/>-kontraktet.
///
/// <para>Core vet ingenting om Roslyn — Roslyn-referensen finns BARA i Synthtax.Analysis-projektet.
/// Core ser bara <c>IAnalysisPlugin</c>, <c>AnalysisRequest</c> och <c>RawIssue</c>.</para>
/// </summary>
public sealed class CSharpRoslynPlugin : AnalysisPluginBase
{
    public override string PluginId     { get; } = "csharp-roslyn";
    public override string DisplayName  { get; } = "C# Roslyn Analyzer";
    public override string Version      { get; } = "2.0.0";

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".cs"];

    public override IReadOnlyList<IPluginRule> Rules { get; } =
    [
        new PluginRuleDescriptor
        {
            RuleId          = "CA001",
            Name            = "Long Method",
            Description     = "Method exceeds 50 lines.",
            Category        = "Maintainability",
            DefaultSeverity = Severity.Medium
        },
        new PluginRuleDescriptor
        {
            RuleId          = "CA002",
            Name            = "Dead Variable",
            Description     = "Variable is declared but never read.",
            Category        = "Code quality",
            DefaultSeverity = Severity.Low
        }
    ];

    public CSharpRoslynPlugin(ILogger<CSharpRoslynPlugin> logger) : base(logger) { }

    protected override async Task<IReadOnlyList<RawIssue>> AnalyzeFileAsync(AnalysisRequest request)
    {
        var tree  = CSharpSyntaxTree.ParseText(request.FileContent);
        var root  = await tree.GetRootAsync(request.CancellationToken);
        var issues = new List<RawIssue>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            request.CancellationToken.ThrowIfCancellationRequested();

            var lineSpan = method.GetLocation().GetLineSpan();
            var lines    = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;

            if (lines > 50)
            {
                // Bygg LogicalScope från syntaxnoden — Roslyn-logiken stannar i plugin-lagret
                var classNode   = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
                var nsNode      = method.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()
                               ?? (SyntaxNode?)method.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
                var ns          = nsNode switch
                {
                    NamespaceDeclarationSyntax n          => n.Name.ToString(),
                    FileScopedNamespaceDeclarationSyntax n => n.Name.ToString(),
                    _                                     => null
                };

                var scope = LogicalScope.ForMethod(
                    ns:         ns,
                    className:  classNode?.Identifier.Text,
                    methodName: method.Identifier.Text);

                issues.Add(MakeIssue(
                    ruleId:    "CA001",
                    scope:     scope,
                    filePath:  request.FilePath,
                    startLine: lineSpan.StartLinePosition.Line + 1,
                    endLine:   lineSpan.EndLinePosition.Line + 1,
                    snippet:   method.Identifier.Text + method.ParameterList.ToString(),
                    message:   $"Metoden '{method.Identifier.Text}' är {lines} rader lång.",
                    severity:  Severity.Medium,
                    category:  "Maintainability",
                    suggestion: "Extrahera hjälpmetoder för att hålla varje metod under 50 rader.",
                    metadata:  new Dictionary<string, string>
                    {
                        ["line_count"]  = lines.ToString(),
                        ["max_allowed"] = "50"
                    }));
            }
        }

        return issues.AsReadOnly();
    }
}
