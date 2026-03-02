using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.Package;

namespace Synthtax.Vsix.Analyzers;

/// <summary>
/// Roslyn DiagnosticAnalyzer som synkroniserar Synthtax backlog-ärenden
/// till Visual Studios Error List och editor-squiggles.
///
/// <para><b>Arkitektur:</b>
/// Analysatorn är medveten om att issues redan är beräknade av Synthtax API
/// — den analyserar INTE syntax/semantik lokalt. Den mappar istället
/// API-svar till <see cref="Diagnostic"/>-instanser med korrekt placering.
/// </para>
///
/// <para><b>Asynkronitet:</b>
/// <list type="bullet">
///   <item>API-anrop sker på bakgrundstråd, aldrig på UI-tråden.</item>
///   <item>Squiggles registreras via CompilationStartAction + RegisterAdditionalFileAction.</item>
///   <item>Cache (filsökväg → diagnostik) lever per IncrementalAnalyzerSession.</item>
/// </list>
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SynthtaxDiagnosticProvider : DiagnosticAnalyzer
{
    // ── Diagnostik som denna analyzer kan rapportera ───────────────────────
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            SynthtaxDiagnosticIds.SA001_NotImplemented,
            SynthtaxDiagnosticIds.SA002_MultipleTypes,
            SynthtaxDiagnosticIds.SA003_ComplexMethod,
            SynthtaxDiagnosticIds.SXGenericCritical,
            SynthtaxDiagnosticIds.SXGenericHigh,
            SynthtaxDiagnosticIds.SXGenericMedium);

    // ── Cache: relativ filsökväg → listan av issues ───────────────────────
    // Uppdateras av BacklogRefreshService när backlog hämtas från API.
    private static readonly ConcurrentDictionary<string, IReadOnlyList<BacklogItemDto>> FileIssueCache
        = new(StringComparer.OrdinalIgnoreCase);

    // ── Publik uppdateringsmetod (anropas av ToolWindow vid refresh) ──────

    /// <summary>
    /// Uppdaterar issue-cachen och triggar en re-analys av berörda dokument.
    /// Anropas på bakgrundstråd från <see cref="Client.SynthtaxApiClient"/>.
    /// </summary>
    public static void UpdateCache(IReadOnlyList<BacklogItemDto> allIssues)
    {
        FileIssueCache.Clear();

        foreach (var issue in allIssues)
        {
            var key = NormalizePath(issue.FilePath);
            FileIssueCache.AddOrUpdate(
                key,
                _ => new List<BacklogItemDto> { issue },
                (_, existing) =>
                {
                    var list = new List<BacklogItemDto>(existing) { issue };
                    return list;
                });
        }
    }

    // ── Roslyn-registrering ───────────────────────────────────────────────

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Kör per syntaxträd (fil) — inte per kompilering
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext ctx)
    {
        var filePath = ctx.Tree.FilePath;
        if (string.IsNullOrEmpty(filePath)) return;

        var key = NormalizePath(filePath);
        if (!FileIssueCache.TryGetValue(key, out var issues)) return;

        var sourceText = ctx.Tree.GetText(ctx.CancellationToken);

        foreach (var issue in issues)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();

            var location = BuildLocation(ctx.Tree, sourceText, issue);
            var descriptor = SynthtaxDiagnosticIds.ForRuleId(issue.RuleId, issue.Severity);

            // Välj rätt messageFormat-argument baserat på descriptor
            var diag = descriptor.Id switch
            {
                "SX0001" => Diagnostic.Create(descriptor, location,
                                issue.MemberName ?? "?",
                                issue.ClassName  ?? "?"),
                "SX0002" => Diagnostic.Create(descriptor, location,
                                2,  // placeholder — exakt antal types från metadata
                                issue.ClassName ?? "?"),
                "SX0003" => Diagnostic.Create(descriptor, location,
                                issue.MemberName ?? "?",
                                "?",  // CC-värde från metadata om tillgängligt
                                10),
                _ => Diagnostic.Create(descriptor, location,
                        issue.RuleId, issue.Message)
            };

            ctx.ReportDiagnostic(diag);
        }
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private static Location BuildLocation(SyntaxTree tree, SourceText text, BacklogItemDto issue)
    {
        try
        {
            // Konvertera 1-baserade radnummer till 0-baserade TextSpan
            var lineIndex = Math.Max(0, issue.StartLine - 1);
            if (lineIndex >= text.Lines.Count) return Location.None;

            var line      = text.Lines[lineIndex];
            var spanStart = line.Start;
            var spanEnd   = line.End;

            // Trimma bort inledande whitespace för mer precis squiggle-placering
            var lineText = text.ToString(TextSpan.FromBounds(spanStart, spanEnd));
            var trimmed  = lineText.TrimStart();
            var offset   = lineText.Length - trimmed.Length;

            return Location.Create(tree, TextSpan.FromBounds(spanStart + offset, spanEnd));
        }
        catch
        {
            return Location.None;
        }
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}
