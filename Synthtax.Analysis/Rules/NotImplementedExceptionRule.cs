using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

// ═══════════════════════════════════════════════════════════════════════════
// CA006 — NotImplementedException Detector
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Detekterar <c>throw new NotImplementedException()</c> och genererar
/// en startpunktsimplementering baserat på metodsignaturen.
///
/// <para><b>Vad som flaggas:</b>
/// <list type="bullet">
///   <item><c>throw new NotImplementedException();</c></item>
///   <item><c>throw new NotImplementedException("message");</c></item>
///   <item><c>=> throw new NotImplementedException();</c> (expression body)</item>
/// </list>
/// </para>
///
/// <para><b>Genererad startpunktkod baseras på:</b>
/// <list type="bullet">
///   <item>Returtypen — returnerar default(T) om okänd.</item>
///   <item>Parameternamn — återanvänds i genererad kod.</item>
///   <item>XML-dokumentation på metoden (om tillgänglig).</item>
///   <item>Klass-kontext — async-metoder får await Task.CompletedTask-body.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class NotImplementedExceptionRule
{
    internal const string RuleId = "CA006";

    internal static IEnumerable<RawIssue> Analyze(
        SyntaxNode root,
        string     filePath,
        CancellationToken ct)
    {
        // Hitta alla `throw new NotImplementedException(...)` noder
        foreach (var throwStmt in root.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            if (!IsNotImplementedException(throwStmt.Expression)) continue;

            var issue = BuildIssue(throwStmt, filePath, throwStmt.GetLocation());
            if (issue != null) yield return issue;
        }

        // Hantera expression-body-metoder: `=> throw new NotImplementedException()`
        foreach (var throwExpr in root.DescendantNodes().OfType<ThrowExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            if (!IsNotImplementedException(throwExpr.Expression)) continue;

            var issue = BuildIssue(throwExpr, filePath, throwExpr.GetLocation());
            if (issue != null) yield return issue;
        }
    }

    // ── Privata hjälpmetoder ──────────────────────────────────────────────

    private static bool IsNotImplementedException(ExpressionSyntax? expr)
    {
        if (expr is not ObjectCreationExpressionSyntax creation) return false;
        var typeName = creation.Type.ToString();
        return typeName == "NotImplementedException" ||
               typeName.EndsWith(".NotImplementedException", StringComparison.Ordinal);
    }

    private static RawIssue? BuildIssue(SyntaxNode throwNode, string filePath, Location location)
    {
        var lineSpan  = location.GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line + 1;

        // Hitta omslutande metod eller property
        var methodNode = FindEnclosingMember(throwNode);
        if (methodNode is null) return null;

        var (ns, className, memberName, scopeKind) = ExtractScope(methodNode);
        var scope = new LogicalScope
        {
            Namespace  = ns,
            ClassName  = className,
            MemberName = memberName,
            Kind       = scopeKind
        };

        var signature    = GetMemberSignature(methodNode);
        var xmlDoc       = GetXmlDocSummary(methodNode);
        var starterCode  = GenerateStarterCode(methodNode, memberName ?? "Method");
        var snippet      = signature;

        var message = xmlDoc is not null
            ? $"Method `{memberName}` throws NotImplementedException. " +
              $"Doc says: \"{TruncateDoc(xmlDoc)}\""
            : $"Method `{memberName}` throws NotImplementedException — unfinished implementation.";

        return new RawIssue
        {
            RuleId        = RuleId,
            Scope         = scope,
            FilePath      = filePath,
            StartLine     = startLine,
            EndLine       = lineSpan.EndLinePosition.Line + 1,
            Snippet       = snippet,
            Message       = message,
            Suggestion    = "Replace the throw with a working implementation. " +
                            "Use the method signature and documentation to infer the behavior.",
            Severity      = Severity.High,
            Category      = "Completeness",
            IsAutoFixable = starterCode is not null,
            FixedSnippet  = starterCode,
            Metadata      = BuildMetadata(xmlDoc, signature)
        };
    }

    private static SyntaxNode? FindEnclosingMember(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(a =>
            a is MethodDeclarationSyntax or
                 PropertyDeclarationSyntax or
                 ConstructorDeclarationSyntax or
                 OperatorDeclarationSyntax or
                 ConversionOperatorDeclarationSyntax);

    private static (string? Ns, string? ClassName, string? MemberName, ScopeKind Kind)
        ExtractScope(SyntaxNode member)
    {
        var classNode = member.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        var nsNode = member.Ancestors()
            .Where(n => n is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
            .FirstOrDefault();

        var ns = nsNode switch
        {
            NamespaceDeclarationSyntax n          => n.Name.ToString(),
            FileScopedNamespaceDeclarationSyntax n => n.Name.ToString(),
            _                                     => null
        };

        var className = classNode?.Identifier.Text;

        return member switch
        {
            MethodDeclarationSyntax m        => (ns, className, m.Identifier.Text, ScopeKind.Method),
            PropertyDeclarationSyntax p      => (ns, className, p.Identifier.Text, ScopeKind.Property),
            ConstructorDeclarationSyntax c   => (ns, className, ".ctor",            ScopeKind.Constructor),
            OperatorDeclarationSyntax o      => (ns, className, "operator",         ScopeKind.Method),
            _                                => (ns, className, null,               ScopeKind.Unknown)
        };
    }

    private static string GetMemberSignature(SyntaxNode member) => member switch
    {
        MethodDeclarationSyntax m =>
            $"{GetModifiers(m.Modifiers)} {m.ReturnType} {m.Identifier.Text}{m.TypeParameterList}{m.ParameterList}",
        PropertyDeclarationSyntax p =>
            $"{GetModifiers(p.Modifiers)} {p.Type} {p.Identifier.Text}",
        ConstructorDeclarationSyntax c =>
            $"{GetModifiers(c.Modifiers)} {c.Identifier.Text}{c.ParameterList}",
        _ =>
            member.ToString()[..Math.Min(120, member.ToString().Length)]
    };

    private static string GetModifiers(SyntaxTokenList mods) =>
        string.Join(" ", mods.Select(m => m.Text));

    private static string? GetXmlDocSummary(SyntaxNode member)
    {
        var trivia = member.GetLeadingTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                              || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        if (trivia == default) return null;

        var xml  = trivia.ToString();
        var start = xml.IndexOf("<summary>",  StringComparison.OrdinalIgnoreCase);
        var end   = xml.IndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < 0 || end <= start) return null;

        var raw = xml[(start + 9)..end];
        // Strip /// prefixes and whitespace
        return string.Join(" ", raw.Split('\n')
            .Select(l => l.TrimStart().TrimStart('/', ' '))
            .Where(l => l.Length > 0));
    }

    /// <summary>
    /// Genererar en startpunktimplementering baserat på metodsignaturen.
    ///
    /// <para>Strategi per returtyp:
    /// <list type="bullet">
    ///   <item>void / Task → tom body med TODO-kommentar</item>
    ///   <item>bool → return false;</item>
    ///   <item>string → return string.Empty;</item>
    ///   <item>IEnumerable/List → return new List&lt;T&gt;();</item>
    ///   <item>Nullable → return null;</item>
    ///   <item>T (value type) → return default;</item>
    /// </list>
    /// </para>
    /// </summary>
    private static string? GenerateStarterCode(SyntaxNode member, string memberName)
    {
        if (member is not MethodDeclarationSyntax method) return null;

        var returnType = method.ReturnType.ToString().Trim();
        var isAsync    = method.Modifiers.Any(SyntaxKind.AsyncKeyword);
        var isVoid     = returnType is "void" or "Task";
        var sb         = new System.Text.StringBuilder();

        // Metodhuvud utan kropp
        var modifiers = string.Join(" ", method.Modifiers.Select(m => m.Text));
        sb.Append($"{modifiers} {returnType} {method.Identifier.Text}{method.TypeParameterList}{method.ParameterList}");

        if (method.ConstraintClauses.Any())
            sb.Append(" " + string.Join(" ", method.ConstraintClauses));

        sb.AppendLine();
        sb.AppendLine("{");

        // TODO-kommentar
        sb.AppendLine($"    // TODO: Implement {memberName}");
        sb.AppendLine($"    // See method documentation for expected behavior.");
        sb.AppendLine();

        // Returtyps-specifik body
        if (isVoid)
        {
            if (isAsync) sb.AppendLine("    await Task.CompletedTask;");
            // void: ingen return krävs
        }
        else if (isAsync && returnType.StartsWith("Task<", StringComparison.Ordinal))
        {
            var innerType = returnType[5..^1]; // Task<T> → T
            sb.AppendLine($"    return {GetDefaultReturn(innerType)};");
        }
        else
        {
            sb.AppendLine($"    return {GetDefaultReturn(returnType)};");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private static string GetDefaultReturn(string typeName) => typeName.TrimEnd('?') switch
    {
        "bool"    => "false",
        "string"  => "string.Empty",
        "int" or "long" or "double" or "float" or "decimal"
                  => "0",
        "Guid"    => "Guid.Empty",
        var t when t.StartsWith("IEnumerable<", StringComparison.Ordinal)
                  => $"Enumerable.Empty<{t[12..^1]}>()",
        var t when t.StartsWith("IReadOnlyList<", StringComparison.Ordinal)
                  => $"Array.Empty<{t[14..^1]}>()",
        var t when t.StartsWith("List<", StringComparison.Ordinal)
                  => $"new List<{t[5..^1]}>()",
        var t when t.EndsWith("?", StringComparison.Ordinal)
                  => "null",
        _         => "default"
    };

    private static IReadOnlyDictionary<string, string> BuildMetadata(string? xmlDoc, string signature)
    {
        var dict = new Dictionary<string, string>
        {
            ["signature"] = signature[..Math.Min(200, signature.Length)]
        };
        if (xmlDoc is not null)
            dict["xml_doc"] = xmlDoc[..Math.Min(300, xmlDoc.Length)];
        return dict;
    }

    private static string TruncateDoc(string doc) =>
        doc.Length > 80 ? doc[..77] + "…" : doc;
}
