using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

/// <summary>
/// <b>SA002 — Multiple Type Declarations in Single File</b>
///
/// <para>Flaggar C#-filer som innehåller mer än en top-level typdeklaration.
/// Varje klass, interface, record, enum eller delegate bör ha en egen fil.</para>
///
/// <para><b>Undantag (ej flaggas):</b>
/// <list type="bullet">
///   <item>Nästlade typer (nested classes) — dessa tillhör sin parent-typ.</item>
///   <item>Partial-klasser — de tillhör samma logiska typ.</item>
///   <item>Privata/internal record-typer som är ResultDto:er (suffix: Dto, Result, Options, Config)
///         och är ≤10 rader — vanlig kompakt DTO-pattern.</item>
/// </list>
/// </para>
/// </summary>
public sealed class MultiClassFileRule
{
    public const string RuleId = "SA002";

    // Typer med dessa suffix tillåts i samma fil (kompakta DTOs/Value Objects)
    private static readonly HashSet<string> AllowedCohabitationSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dto", "Result", "Request", "Response", "Options", "Config", "Settings",
        "Event", "Command", "Query", "Args"
    };

    public IReadOnlyList<RawIssue> Analyze(
        SyntaxTree            tree,
        string                filePath,
        IReadOnlySet<string>? enabledRules = null)
    {
        if (enabledRules is not null && !enabledRules.Contains(RuleId)) return [];

        var root = tree.GetRoot();

        // Hämta bara TOP-LEVEL typdeklarationer (inte nästlade)
        var topLevelTypes = root
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .Where(t => t.Parent is not TypeDeclarationSyntax) // uteslut nästlade
            .ToList();

        // Lägg till top-level enums och delegates
        var topLevelEnums = root
            .DescendantNodes()
            .OfType<EnumDeclarationSyntax>()
            .Where(e => e.Parent is not TypeDeclarationSyntax)
            .ToList();

        var topLevelDelegates = root
            .DescendantNodes()
            .OfType<DelegateDeclarationSyntax>()
            .Where(d => d.Parent is not TypeDeclarationSyntax)
            .ToList();

        // Filtrera ut partial-klasser om det bara finns en icke-partial version
        var nonPartialTypes = topLevelTypes
            .Where(t => !t.Modifiers.Any(m => m.Text == "partial"))
            .ToList();

        var partialGroups = topLevelTypes
            .Where(t => t.Modifiers.Any(m => m.Text == "partial"))
            .GroupBy(t => t.Identifier.Text)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Skip(1)) // första partial-filen är ok
            .ToList();

        // Alla signifikanta typer
        var significantTypes = nonPartialTypes
            .Cast<MemberDeclarationSyntax>()
            .Concat(topLevelEnums)
            .Concat(topLevelDelegates)
            .ToList();

        if (significantTypes.Count <= 1) return [];

        // Kolla om alla sekundära typer är tillåtna "cohabitators"
        var primaryType   = significantTypes[0];
        var secondaryTypes = significantTypes.Skip(1).ToList();

        var violations = secondaryTypes
            .Where(t => !IsAllowedCohabitor(t))
            .ToList();

        if (!violations.Any()) return [];

        // ── Bygg en issue per sekundär typ som bör extraheras ─────────────
        var issues      = new List<RawIssue>();
        var primaryName = GetTypeName(primaryType);
        var ns          = GetNamespace(primaryType);

        var violationNames = violations.Select(GetTypeName).ToList();
        var allTypeNames   = significantTypes.Select(GetTypeName).ToList();

        foreach (var violation in violations)
        {
            var typeName    = GetTypeName(violation);
            var expectedFile = $"{typeName}.cs";
            var lineSpan    = tree.GetLineSpan(violation.Span);

            issues.Add(new RawIssue
            {
                RuleId    = RuleId,
                Scope     = LogicalScope.ForClass(ns, typeName),
                FilePath  = filePath,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine   = lineSpan.EndLinePosition.Line   + 1,
                Snippet   = TruncateDeclaration(violation.ToString()),
                Message   = $"Type '{typeName}' shares file '{Path.GetFileName(filePath)}' " +
                            $"with '{primaryName}' and {secondaryTypes.Count - 1} other types. " +
                            $"Extract to '{expectedFile}'.",
                Suggestion = $"Move '{typeName}' to a new file named '{expectedFile}' " +
                             $"in the same namespace '{ns ?? "unknown"}'.",
                Severity  = Severity.Medium,
                Category  = "Structure",
                IsAutoFixable = false,
                Metadata  = new Dictionary<string, string>
                {
                    ["allTypesInFile"]    = string.Join(", ", allTypeNames),
                    ["suggestedFileName"] = expectedFile,
                    ["primaryType"]       = primaryName
                }
            });
        }

        return issues.AsReadOnly();
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private static bool IsAllowedCohabitor(MemberDeclarationSyntax type)
    {
        var name = GetTypeName(type);

        // Tillåt om suffixet matchar och typen är liten (≤10 rader)
        if (AllowedCohabitationSuffixes.Any(suffix =>
            name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            var lineCount = type.ToString().Split('\n').Length;
            if (lineCount <= 12) return true;
        }

        // Tillåt privata/internal enums (flag-patterns)
        if (type is EnumDeclarationSyntax enumDecl)
        {
            var hasPrivateOrInternal = enumDecl.Modifiers
                .Any(m => m.Text is "private" or "internal");
            if (hasPrivateOrInternal) return true;
        }

        return false;
    }

    private static string GetTypeName(MemberDeclarationSyntax node) => node switch
    {
        TypeDeclarationSyntax t => t.Identifier.Text,
        EnumDeclarationSyntax e => e.Identifier.Text,
        DelegateDeclarationSyntax d => d.Identifier.Text,
        _ => "Unknown"
    };

    private static string? GetNamespace(MemberDeclarationSyntax node)
    {
        var nsDecl = node.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault()
                     ?? (SyntaxNode?)node.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault();
        return nsDecl switch
        {
            NamespaceDeclarationSyntax ns => ns.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax fns => fns.Name.ToString(),
            _ => null
        };
    }

    private static string TruncateDeclaration(string declaration)
    {
        var lines = declaration.Split('\n');
        // Visa bara signaturen (max 5 rader)
        return lines.Length <= 5
            ? declaration
            : string.Join('\n', lines.Take(5)) + "\n    // ...";
    }
}
