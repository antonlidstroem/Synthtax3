using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

// ═══════════════════════════════════════════════════════════════════════════
// CA008 — Method Extraction (Complex Methods)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Identifierar metoder som bör delas upp i mindre enheter genom att
/// kombinera tre komplexitetsmått:
///
/// <list type="bullet">
///   <item><b>Cyklomatisk komplexitet:</b> antal oberoende kodvägar (if, for, while, catch, &&, ||, ??)</item>
///   <item><b>Kognitiv komplexitet:</b> hur svår koden är att förstå för en människa (nästling bestraffas)</item>
///   <item><b>Radräkning:</b> metodens totala längd i källkod</item>
/// </list>
///
/// <para><b>Tröskel-kriterier (flagga om MINST TVÅ uppfylls):</b>
/// <list type="table">
///   <listheader><term>Mått</term><term>Varning</term><term>Kritisk</term></listheader>
///   <item><term>Cyklomatisk</term><term>≥ 8</term><term>≥ 15</term></item>
///   <item><term>Kognitiv</term>   <term>≥ 10</term><term>≥ 20</term></item>
///   <item><term>Rader</term>      <term>≥ 40</term><term>≥ 80</term></item>
/// </list>
/// </para>
/// </summary>
internal sealed class MethodExtractionRule
{
    internal const string RuleId = "CA008";

    // Tröskelkonstanter (justerbara via config)
    private const int CyclomaticWarn     = 8;
    private const int CyclomaticCritical = 15;
    private const int CognitiveWarn      = 10;
    private const int CognitiveCritical  = 20;
    private const int LineCountWarn      = 40;
    private const int LineCountCritical  = 80;
    private const int ReturnCountWarn    = 4;

    internal static IEnumerable<RawIssue> Analyze(
        SyntaxNode root,
        string     filePath,
        CancellationToken ct)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            // Hoppa över abstrakta/interface-metoder (ingen kropp)
            if (method.Body is null && method.ExpressionBody is null) continue;

            // Hoppa över trivellt korta metoder
            var lineSpan  = method.GetLocation().GetLineSpan();
            var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
            if (lineCount < 10) continue;

            var cyclomatic = ComputeCyclomatic(method);
            var cognitive  = ComputeCognitive(method);
            var returns    = CountReturnStatements(method);

            // Bedöm hur många kriterier som är uppfyllda
            int warnings  = CountWarnings(cyclomatic, cognitive, lineCount, returns);
            int criticals = CountCriticals(cyclomatic, cognitive, lineCount);

            if (warnings < 2 && criticals < 1) continue;

            var severity = criticals >= 2 ? Severity.High
                         : criticals >= 1 ? Severity.Medium
                         :                  Severity.Low;

            // Bygg scope
            var classNode = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var nsNode    = method.Ancestors()
                .Where(n => n is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
                .FirstOrDefault();

            var ns = nsNode switch
            {
                NamespaceDeclarationSyntax n          => n.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax n => n.Name.ToString(),
                _                                     => null
            };

            var scope = LogicalScope.ForMethod(
                ns, classNode?.Identifier.Text, method.Identifier.Text);

            var snippet   = BuildComplexitySnapshot(method, lineCount);
            var breakdown = BuildBreakdown(cyclomatic, cognitive, lineCount, returns);

            yield return new RawIssue
            {
                RuleId     = RuleId,
                Scope      = scope,
                FilePath   = filePath,
                StartLine  = lineSpan.StartLinePosition.Line + 1,
                EndLine    = lineSpan.EndLinePosition.Line + 1,
                Snippet    = snippet,
                Message    = $"Metoden `{method.Identifier.Text}` är komplex: {breakdown}.",
                Suggestion = "Identifiera distinkta logiska faser och extrahera dem till " +
                             "privata hjälpmetoder med beskrivande namn. " +
                             "Publika API förblir oförändrat.",
                Severity   = severity,
                Category   = "Maintainability",
                Metadata   = new Dictionary<string, string>
                {
                    ["complexity"]    = cognitive.ToString(),
                    ["cyclomatic"]    = cyclomatic.ToString(),
                    ["line_count"]    = lineCount.ToString(),
                    ["return_count"]  = returns.ToString(),
                    ["max_complexity"] = CyclomaticWarn.ToString()
                }
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Komplexitetsmätare
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cyklomatisk komplexitet = antal beslutsvägar + 1.
    /// Räknar: if, else if, for, foreach, while, do, case, catch, && (i conditions), ||, ??
    /// </summary>
    private static int ComputeCyclomatic(MethodDeclarationSyntax method)
    {
        int count = 1; // Grundkomplexitet

        foreach (var node in method.DescendantNodes())
        {
            count += node switch
            {
                IfStatementSyntax           => 1,
                ElseClauseSyntax e when e.Statement is not IfStatementSyntax => 0,
                ForStatementSyntax          => 1,
                ForEachStatementSyntax      => 1,
                WhileStatementSyntax        => 1,
                DoStatementSyntax           => 1,
                CatchClauseSyntax           => 1,
                SwitchSectionSyntax         => 1,
                SwitchExpressionArmSyntax   => 1,
                ConditionalExpressionSyntax => 1,  // ternary
                BinaryExpressionSyntax b when
                    b.IsKind(SyntaxKind.LogicalAndExpression) ||
                    b.IsKind(SyntaxKind.LogicalOrExpression)  => 1,
                BinaryExpressionSyntax b when
                    b.IsKind(SyntaxKind.CoalesceExpression)   => 1,
                _ => 0
            };
        }
        return count;
    }

    /// <summary>
    /// Kognitiv komplexitet (förenklad variant av Sonar-algoritmen):
    /// Incrementerar baserat på nästlingsnivå — djupt nästlad logik är svårare att förstå.
    /// </summary>
    private static int ComputeCognitive(MethodDeclarationSyntax method)
    {
        int total = 0;

        void Walk(SyntaxNode node, int nesting)
        {
            int increment = 0;
            int childNesting = nesting;

            switch (node)
            {
                case IfStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                    increment    = 1 + nesting;
                    childNesting = nesting + 1;
                    break;

                case ElseClauseSyntax e when e.Statement is IfStatementSyntax:
                    increment    = 1;  // else-if: inget extra nesting-straff
                    childNesting = nesting;
                    break;

                case ElseClauseSyntax:
                    increment    = 1;
                    childNesting = nesting + 1;
                    break;

                case CatchClauseSyntax:
                    increment    = 1 + nesting;
                    childNesting = nesting + 1;
                    break;

                case SwitchStatementSyntax:
                case SwitchExpressionSyntax:
                    increment    = 1 + nesting;
                    childNesting = nesting + 1;
                    break;

                case ConditionalExpressionSyntax:
                    increment    = 1 + nesting;
                    childNesting = nesting + 1;
                    break;

                case BinaryExpressionSyntax b when
                    b.IsKind(SyntaxKind.LogicalAndExpression) ||
                    b.IsKind(SyntaxKind.LogicalOrExpression):
                    increment = 1;
                    break;

                case LocalFunctionStatementSyntax:
                case ParenthesizedLambdaExpressionSyntax:
                case SimpleLambdaExpressionSyntax:
                    increment    = 1;
                    childNesting = nesting + 1;
                    break;
            }

            total += increment;
            foreach (var child in node.ChildNodes())
                Walk(child, childNesting);
        }

        if (method.Body      is not null) Walk(method.Body, 0);
        if (method.ExpressionBody is not null) Walk(method.ExpressionBody, 0);
        return total;
    }

    private static int CountReturnStatements(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<ReturnStatementSyntax>().Count();

    // ── Bedömningshjälpare ────────────────────────────────────────────────

    private static int CountWarnings(int cyclo, int cog, int lines, int returns) =>
        (cyclo  >= CyclomaticWarn  ? 1 : 0)  +
        (cog    >= CognitiveWarn   ? 1 : 0)  +
        (lines  >= LineCountWarn   ? 1 : 0)  +
        (returns >= ReturnCountWarn ? 1 : 0);

    private static int CountCriticals(int cyclo, int cog, int lines) =>
        (cyclo >= CyclomaticCritical ? 1 : 0) +
        (cog   >= CognitiveCritical  ? 1 : 0) +
        (lines >= LineCountCritical  ? 1 : 0);

    private static string BuildBreakdown(int cyclo, int cog, int lines, int returns) =>
        $"cyklomatisk={cyclo}" +
        (cyclo >= CyclomaticCritical ? "🔴" : cyclo >= CyclomaticWarn ? "🟠" : "") +
        $", kognitiv={cog}" +
        (cog >= CognitiveCritical ? "🔴" : cog >= CognitiveWarn ? "🟠" : "") +
        $", rader={lines}" +
        (lines >= LineCountCritical ? "🔴" : lines >= LineCountWarn ? "🟠" : "") +
        $", returer={returns}";

    /// <summary>Bygger ett kodfragment som visar metodens signatur och första/sista rader.</summary>
    private static string BuildComplexitySnapshot(MethodDeclarationSyntax method, int lineCount)
    {
        var sig  = $"{string.Join(" ", method.Modifiers.Select(m => m.Text))} " +
                   $"{method.ReturnType} {method.Identifier.Text}{method.TypeParameterList}{method.ParameterList}";

        if (method.Body is null) return sig;

        var stmts = method.Body.Statements;
        if (stmts.Count == 0) return sig + " { }";

        // Visa signatur + första 3 + "..." + sista 2 satser
        var first = stmts.Take(3).Select(s => "    " + s.ToString().Split('\n')[0].Trim());
        var more  = stmts.Count > 5 ? [$"    // ... ({stmts.Count - 5} statements)"] : [];
        var last  = stmts.Count > 5 ? stmts.TakeLast(2).Select(s => "    " + s.ToString().Split('\n')[0].Trim()) : [];

        return sig + "\n{\n" + string.Join("\n", first.Concat(more).Concat(last)) + "\n}";
    }
}
