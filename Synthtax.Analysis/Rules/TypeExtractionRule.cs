using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

// ═══════════════════════════════════════════════════════════════════════════
// CA007 — Type Extraction (Multiple Types Per File)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Flaggar filer som innehåller fler än en toppnivå-typdeklaration
/// och föreslår utbrytning till separata filer.
///
/// <para><b>Primärtyp:</b>
/// Typen som matchar filnamnet (utan .cs) betraktas som primär och ska stanna.
/// Alla övriga typer är kandidater för utbrytning.</para>
///
/// <para><b>Undantag (flaggas EJ):</b>
/// <list type="bullet">
///   <item>Nästlade typer (inner classes) — de är avsiktligt nästlade.</item>
///   <item><c>partial</c>-klasser — delade delar av samma typ.</item>
///   <item>Filer med bara enum-deklarationer (ibland legitimt att gruppera).</item>
///   <item>Designer-generated filer (<c>*.Designer.cs</c>, <c>*.g.cs</c>).</item>
/// </list>
/// </para>
/// </summary>
internal sealed class TypeExtractionRule
{
    internal const string RuleId = "CA007";

    internal static IEnumerable<RawIssue> Analyze(
        SyntaxNode root,
        string     filePath,
        CancellationToken ct)
    {
        // Hoppa över genererade filer
        if (IsGeneratedFile(filePath)) yield break;

        // Hämta bara toppnivå-typer (ej nästlade)
        var topLevelTypes = GetTopLevelTypes(root, ct).ToList();

        if (topLevelTypes.Count <= 1) yield break;

        // Filtrera bort partial-typer (alla partial-deklarationer räknas som en typ)
        var nonPartialGroups = topLevelTypes
            .Where(t => !t.Modifiers.Any(SyntaxKind.PartialKeyword))
            .ToList();

        // Om alla är partial → ingen flaggning
        if (nonPartialGroups.Count <= 1) yield break;

        // Bestäm primärtypen (matchar filnamnet)
        var fileBaseName = System.IO.Path.GetFileNameWithoutExtension(filePath);
        var primaryType  = topLevelTypes
            .FirstOrDefault(t => string.Equals(t.Identifier.Text, fileBaseName, StringComparison.OrdinalIgnoreCase))
            ?? topLevelTypes.First();

        var extraTypes = topLevelTypes
            .Where(t => t != primaryType)
            .ToList();

        if (extraTypes.Count == 0) yield break;

        // Bygg namespace-kontext för scope
        var nsNode = root.DescendantNodes()
            .Where(n => n is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
            .FirstOrDefault();

        var ns = nsNode switch
        {
            NamespaceDeclarationSyntax n          => n.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax n => n.Name.ToString(),
            _                                     => null
        };

        var scope = LogicalScope.ForClass(ns, primaryType.Identifier.Text);

        var extraTypeNames = string.Join(", ", extraTypes.Select(t =>
            $"`{t.Keyword.Text} {t.Identifier.Text}`"));

        var snippet = BuildFileSnapshot(topLevelTypes);

        yield return new RawIssue
        {
            RuleId     = RuleId,
            Scope      = scope,
            FilePath   = filePath,
            StartLine  = 1,
            EndLine    = root.GetLocation().GetLineSpan().EndLinePosition.Line + 1,
            Snippet    = snippet,
            Message    = $"`{System.IO.Path.GetFileName(filePath)}` innehåller " +
                         $"{topLevelTypes.Count} typer: primär `{primaryType.Identifier.Text}` " +
                         $"+ {extraTypes.Count} extra: {extraTypeNames}.",
            Suggestion = $"Flytta {extraTypeNames} till separata .cs-filer med matchande namn. " +
                         $"Behåll bara `{primaryType.Identifier.Text}` i {System.IO.Path.GetFileName(filePath)}.",
            Severity   = Severity.Medium,
            Category   = "Architecture",
            Metadata   = new Dictionary<string, string>
            {
                ["class_count"]  = topLevelTypes.Count.ToString(),
                ["primary_type"] = primaryType.Identifier.Text,
                ["extra_types"]  = string.Join(";", extraTypes.Select(t => t.Identifier.Text))
            }
        };
    }

    // ── Privata hjälpmetoder ──────────────────────────────────────────────

    private static IEnumerable<TypeDeclarationSyntax> GetTopLevelTypes(
        SyntaxNode root, CancellationToken ct)
    {
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            // Hoppa över nästlade typer — de har en annan TypeDeclaration som förälder
            var isNested = type.Ancestors().OfType<TypeDeclarationSyntax>().Any();
            if (!isNested) yield return type;
        }
    }

    private static bool IsGeneratedFile(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        return fileName.EndsWith(".Designer.cs",  StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.cs",          StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".g.i.cs",        StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bygger ett kompakt overview-snippet som visar alla typers signaturer.
    /// Inkluderar inte full implementering — bara deklarationsraderna.
    /// </summary>
    private static string BuildFileSnapshot(List<TypeDeclarationSyntax> types)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var type in types)
        {
            var modifiers = string.Join(" ", type.Modifiers.Select(m => m.Text));
            var baseList  = type.BaseList?.ToString() ?? "";
            sb.AppendLine($"// ── Type: {type.Identifier.Text} ──");
            sb.AppendLine($"{modifiers} {type.Keyword.Text} {type.Identifier.Text}{type.TypeParameterList}{baseList}");
            sb.AppendLine($"  // ... ({type.Members.Count} members)");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
