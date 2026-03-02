using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Analysis.Rules;

/// <summary>
/// <b>SA001 — NotImplementedException Detected</b>
///
/// <para>Flaggar metoder/properties som kastar <c>NotImplementedException</c>
/// och genererar starter-kod via <c>FixedSnippet</c> som kan skickas till PromptFactory.</para>
///
/// <para><b>Detekteras:</b>
/// <list type="bullet">
///   <item><c>throw new NotImplementedException();</c> som enda statement.</item>
///   <item><c>throw new NotImplementedException("message");</c>.</item>
///   <item><c>=> throw new NotImplementedException();</c> expression-body.</item>
///   <item>Metoder vars kropp bara innehåller NIE + eventuell kommentar.</item>
/// </list>
/// </para>
/// </summary>
public sealed class NotImplementedExceptionRule
{
    public const string RuleId = "SA001";

    public IReadOnlyList<RawIssue> Analyze(
        SyntaxTree      tree,
        SemanticModel?  model,
        string          filePath,
        IReadOnlySet<string>? enabledRules = null)
    {
        if (enabledRules is not null && !enabledRules.Contains(RuleId)) return [];

        var root   = tree.GetRoot();
        var issues = new List<RawIssue>();

        // ── 1. Metoddeklarationer med NIE-kropp ───────────────────────────
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!IsNieBody(method.Body, method.ExpressionBody)) continue;

            var classNode = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var ns        = GetNamespace(method);
            var starter   = BuildStarterCode(method, model);

            issues.Add(new RawIssue
            {
                RuleId    = RuleId,
                Scope     = LogicalScope.ForMethod(ns, classNode?.Identifier.Text, method.Identifier.Text),
                FilePath  = filePath,
                StartLine = tree.GetLineSpan(method.Span).StartLinePosition.Line + 1,
                EndLine   = tree.GetLineSpan(method.Span).EndLinePosition.Line   + 1,
                Snippet   = method.ToString(),
                Message   = $"Method '{method.Identifier.Text}' throws NotImplementedException — " +
                            "this is an unfinished stub that must be implemented.",
                Suggestion = "Implement the method body. Use the FixedSnippet as a starting point " +
                             "and fill in the actual business logic.",
                Severity  = Severity.High,
                Category  = "Implementation",
                IsAutoFixable = true,
                FixedSnippet  = starter
            });
        }

        // ── 2. Property-accessors ─────────────────────────────────────────
        foreach (var prop in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            var getter = prop.AccessorList?.Accessors
                .FirstOrDefault(a => a.Kind() == SyntaxKind.GetAccessorDeclaration);

            if (getter is null) continue;
            if (!IsNieBody(getter.Body, getter.ExpressionBody)) continue;

            var classNode = prop.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            var ns        = GetNamespace(prop);

            issues.Add(new RawIssue
            {
                RuleId    = RuleId,
                Scope     = LogicalScope.ForMember(ns, classNode?.Identifier.Text,
                                prop.Identifier.Text, ScopeKind.Property),
                FilePath  = filePath,
                StartLine = tree.GetLineSpan(prop.Span).StartLinePosition.Line + 1,
                EndLine   = tree.GetLineSpan(prop.Span).EndLinePosition.Line   + 1,
                Snippet   = prop.ToString(),
                Message   = $"Property '{prop.Identifier.Text}' getter throws NotImplementedException.",
                Suggestion = "Implement the getter to return the correct value.",
                Severity  = Severity.High,
                Category  = "Implementation",
                IsAutoFixable = false
            });
        }

        return issues.AsReadOnly();
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private static bool IsNieBody(BlockSyntax? block, ArrowExpressionClauseSyntax? arrow)
    {
        // Expression-body: => throw new NotImplementedException()
        if (arrow?.Expression is ThrowExpressionSyntax throwExpr)
            return IsNieThrow(throwExpr.Expression);

        if (block is null) return false;

        // Ignorera block som bara har kommentarer och NIE
        var realStatements = block.Statements
            .Where(s => s is not EmptyStatementSyntax)
            .ToList();

        return realStatements.Count == 1
            && realStatements[0] is ThrowStatementSyntax throwStmt
            && IsNieThrow(throwStmt.Expression);
    }

    private static bool IsNieThrow(ExpressionSyntax? expr) =>
        expr is ObjectCreationExpressionSyntax creation &&
        creation.Type.ToString()
            .EndsWith("NotImplementedException", StringComparison.Ordinal);

    /// <summary>
    /// Bygger ett kommenterat starter-skelett för metoden.
    /// Inkluderar: returtyp, parameter-dokumentation och TODO-platshållare.
    /// </summary>
    private static string BuildStarterCode(
        MethodDeclarationSyntax method,
        SemanticModel?          model)
    {
        var sb = new System.Text.StringBuilder();
        var returnType = method.ReturnType.ToString();
        var methodName = method.Identifier.Text;
        var parameters = method.ParameterList.Parameters;

        // Bevara modifiers och signatur
        var modifiers = method.Modifiers.ToString();
        sb.AppendLine($"{modifiers} {returnType} {methodName}({method.ParameterList})");
        sb.AppendLine("{");

        // Parameter null-checks för reference types
        foreach (var param in parameters)
        {
            var typeName = param.Type?.ToString() ?? "";
            if (!typeName.EndsWith("?") &&
                !IsValueType(typeName) &&
                !string.IsNullOrEmpty(param.Identifier.Text))
            {
                sb.AppendLine($"    ArgumentNullException.ThrowIfNull({param.Identifier.Text});");
            }
        }

        // TODO-block
        sb.AppendLine();
        sb.AppendLine("    // TODO: Implement this method.");
        sb.AppendLine($"    // Original intent: [{methodName}] in class [{method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text}]");
        sb.AppendLine($"    // Parameters: {string.Join(", ", parameters.Select(p => $"{p.Type} {p.Identifier}"))}");

        // Returvärde
        var returnDefault = GetDefaultReturn(returnType);
        if (returnDefault != null)
        {
            sb.AppendLine();
            sb.AppendLine($"    return {returnDefault}; // Replace with actual implementation");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private static bool IsValueType(string typeName) =>
        typeName is "int" or "long" or "bool" or "double" or "float"
            or "decimal" or "byte" or "char" or "Guid" or "DateTime"
            or "TimeSpan" or "DateOnly" or "TimeOnly";

    private static string? GetDefaultReturn(string returnType) => returnType switch
    {
        "void"      => null,
        "Task"      => "Task.CompletedTask",
        var t when t.StartsWith("Task<") => $"Task.FromResult({GetDefaultReturn(t[5..^1])})",
        "bool"      => "false",
        "int" or "long" or "double" or "decimal" => "0",
        "string"    => "string.Empty",
        var t when t.EndsWith("?") => "null",
        _           => $"default({returnType})"
    };

    private static string? GetNamespace(SyntaxNode node)
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
}
