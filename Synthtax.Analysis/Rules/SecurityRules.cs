using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Rules;

public sealed class HardcodedCredentialRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId    => "SEC001";
    public string Name      => "Hardcoded Credential";
    public bool   IsEnabled => true;

    private static readonly HashSet<string> CredentialPropertyNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "password","pwd","secret","apikey","api_key","token","connectionstring",
            "private_key","accesskey","access_key","clientsecret","client_secret",
        };

    public IEnumerable<SecurityIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        foreach (var assign in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (assign.Left is not IdentifierNameSyntax id) continue;
            if (!CredentialPropertyNames.Contains(id.Identifier.Text)) continue;
            if (assign.Right is not LiteralExpressionSyntax lit) continue;
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            var val = lit.Token.ValueText;
            if (val.Length < 3 || IsPlaceholder(val)) continue;
            var span = assign.GetLocation().GetLineSpan();
            yield return MakeIssue(fileName, filePath, span.StartLinePosition.Line + 1,
                assign.ToString().Trim(), id.Identifier.Text);
        }

        foreach (var varDecl in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!CredentialPropertyNames.Contains(varDecl.Identifier.Text)) continue;
            if (varDecl.Initializer?.Value is not LiteralExpressionSyntax lit2) continue;
            if (!lit2.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            var val = lit2.Token.ValueText;
            if (val.Length < 3 || IsPlaceholder(val)) continue;
            var span = varDecl.GetLocation().GetLineSpan();
            yield return MakeIssue(fileName, filePath, span.StartLinePosition.Line + 1,
                varDecl.ToString().Trim(), varDecl.Identifier.Text);
        }

        foreach (var arg in root.DescendantNodes().OfType<ArgumentSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (arg.NameColon?.Name is not IdentifierNameSyntax argName) continue;
            if (!CredentialPropertyNames.Contains(argName.Identifier.Text)) continue;
            if (arg.Expression is not LiteralExpressionSyntax lit3) continue;
            if (!lit3.IsKind(SyntaxKind.StringLiteralExpression)) continue;
            var val = lit3.Token.ValueText;
            if (val.Length < 3 || IsPlaceholder(val)) continue;
            var span = arg.GetLocation().GetLineSpan();
            yield return MakeIssue(fileName, filePath, span.StartLinePosition.Line + 1,
                arg.ToString().Trim(), argName.Identifier.Text);
        }
    }

    private static bool IsPlaceholder(string val) =>
        val.Contains("CHANGE", StringComparison.OrdinalIgnoreCase) ||
        val.Contains("TODO",   StringComparison.OrdinalIgnoreCase) ||
        val.Equals("***",      StringComparison.Ordinal);

    private static SecurityIssueDto MakeIssue(
        string fileName, string filePath, int line, string snippet, string propName) =>
        new()
        {
            FilePath       = filePath,
            FileName       = fileName,
            IssueType      = "HardcodedCredential",
            Title          = "Hardcoded Credential Detected",
            Description    = $"Literal value assigned to credential-sensitive identifier '{propName}'.",
            Recommendation = "Move secrets to environment variables, Azure Key Vault, or Secret Manager.",
            LineNumber     = line,
            CodeSnippet    = snippet.Length > 120 ? snippet[..120] : snippet,
            Severity       = Severity.High,
            Category       = "Credentials"
        };
}

public sealed class SqlInjectionRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId    => "SEC002";
    public string Name      => "SQL Injection Risk";
    public bool   IsEnabled => true;

    private static readonly HashSet<string> SqlVariableHints =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "query","sql","commandText","sqlQuery","sqlCommand",
            "queryString","statement","cmdText","command",
        };

    public IEnumerable<SecurityIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var node in root.DescendantNodes())
        {
            ct.ThrowIfCancellationRequested();
            ExpressionSyntax? rhs = null;
            string? name = null;
            int line = 0;
            string snippet = "";

            switch (node)
            {
                case LocalDeclarationStatementSyntax decl:
                    foreach (var v in decl.Declaration.Variables)
                    {
                        if (!SqlVariableHints.Contains(v.Identifier.Text)) continue;
                        if (v.Initializer?.Value is not ExpressionSyntax init) continue;
                        rhs = init; name = v.Identifier.Text;
                        line = v.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        snippet = decl.ToString().Trim();
                    }
                    break;

                case AssignmentExpressionSyntax assign
                    when assign.Left is IdentifierNameSyntax assignId &&
                         SqlVariableHints.Contains(assignId.Identifier.Text):
                    rhs = assign.Right; name = assignId.Identifier.Text;
                    line = assign.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    snippet = assign.ToString().Trim();
                    break;
            }

            if (rhs is null || name is null) continue;
            if (!ContainsTaintedExpression(rhs, model)) continue;

            yield return new SecurityIssueDto
            {
                FilePath       = filePath,
                FileName       = fileName,
                IssueType      = "SqlInjectionRisk",
                Title          = "Potential SQL Injection",
                Description    = $"SQL variable '{name}' is constructed from non-constant values.",
                Recommendation = "Use parameterized queries, stored procedures, or EF Core to prevent SQL injection.",
                LineNumber     = line,
                CodeSnippet    = snippet.Length > 120 ? snippet[..120] : snippet,
                Severity       = Severity.High,
                Category       = "Injection"
            };
        }
    }

    private static bool ContainsTaintedExpression(ExpressionSyntax expr, SemanticModel? model)
    {
        if (model is null)
            return expr is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.AddExpression)
                || expr is InterpolatedStringExpressionSyntax;

        if (expr is not (BinaryExpressionSyntax or InterpolatedStringExpressionSyntax))
            return false;

        foreach (var descendant in expr.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            var constant = model.GetConstantValue(descendant);
            if (constant.HasValue) continue;
            if (descendant is LiteralExpressionSyntax) continue;
            var typeInfo = model.GetTypeInfo(descendant);
            if (typeInfo.Type?.SpecialType == SpecialType.System_String)
            {
                if (descendant is IdentifierNameSyntax idSyn)
                {
                    var sym = model.GetSymbolInfo(idSyn).Symbol;
                    if (sym is IParameterSymbol or IFieldSymbol or IPropertySymbol or ILocalSymbol)
                        return true;
                }
                if (descendant is InvocationExpressionSyntax or MemberAccessExpressionSyntax)
                    return true;
            }
        }
        return false;
    }
}

public sealed class InsecureRandomRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId    => "SEC003";
    public string Name      => "Insecure Random Usage";
    public bool   IsEnabled => true;

    private static readonly HashSet<string> SecurityContextKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "password","token","secret","key","salt","nonce","guid",
            "csrf","xsrf","session","auth","otp","pin","hash","sign",
        };

    public IEnumerable<SecurityIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        foreach (var oc in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRandomType(oc.Type.ToString())) continue;
            var seeded = oc.ArgumentList?.Arguments.Count > 0;
            var span   = oc.GetLocation().GetLineSpan();
            yield return new SecurityIssueDto
            {
                FilePath       = filePath, FileName = fileName,
                IssueType      = "InsecureRandom",
                Title          = seeded ? "Seeded (Deterministic) Random Usage" : "Insecure Random Usage",
                Description    = seeded
                    ? "A seeded System.Random produces deterministic sequences — never use for security."
                    : "System.Random is NOT cryptographically secure.",
                Recommendation = "Use System.Security.Cryptography.RandomNumberGenerator for any security-sensitive context.",
                LineNumber     = span.StartLinePosition.Line + 1,
                CodeSnippet    = oc.ToString().Trim(),
                Severity       = GetSeverity(oc, model),
                Category       = "Cryptography"
            };
        }

        foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!ma.ToString().StartsWith("Random.Shared", StringComparison.OrdinalIgnoreCase)) continue;
            if (ma.Parent is MemberAccessExpressionSyntax) continue;
            var span = ma.GetLocation().GetLineSpan();
            yield return new SecurityIssueDto
            {
                FilePath       = filePath, FileName = fileName,
                IssueType      = "InsecureRandom",
                Title          = "Insecure Random.Shared Usage",
                Description    = "Random.Shared is NOT cryptographically secure.",
                Recommendation = "Use RandomNumberGenerator.GetInt32() or RandomNumberGenerator.Fill() instead.",
                LineNumber     = span.StartLinePosition.Line + 1,
                CodeSnippet    = ma.ToString().Trim(),
                Severity       = GetSeverity(ma, model),
                Category       = "Cryptography"
            };
        }
    }

    private static bool IsRandomType(string typeName) => typeName is "Random" or "System.Random";

    private static Severity GetSeverity(SyntaxNode node, SemanticModel? model)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return Severity.Medium;
        var name = method.Identifier.Text;
        if (SecurityContextKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return Severity.High;
        if (node.Parent is EqualsValueClauseSyntax evc &&
            evc.Parent is VariableDeclaratorSyntax vd &&
            SecurityContextKeywords.Any(k => vd.Identifier.Text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return Severity.High;
        return Severity.Medium;
    }
}

public sealed class MissingCancellationTokenRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId    => "SEC004";
    public string Name      => "Missing CancellationToken";
    public bool   IsEnabled => true;

    public IEnumerable<SecurityIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword))) continue;
            var name = method.Identifier.Text;
            if (name.StartsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)) continue;

            bool hasCt = method.ParameterList.Parameters.Any(p =>
                p.Type?.ToString().Contains("CancellationToken", StringComparison.OrdinalIgnoreCase) == true);
            if (hasCt) continue;

            var span = method.GetLocation().GetLineSpan();
            yield return new SecurityIssueDto
            {
                FilePath       = filePath, FileName = fileName,
                IssueType      = "MissingCancellationToken",
                Title          = "Async Method Missing CancellationToken",
                Description    = $"Async method '{name}' does not accept a CancellationToken parameter.",
                Recommendation = "Add CancellationToken cancellationToken = default as last parameter.",
                LineNumber     = span.StartLinePosition.Line + 1,
                CodeSnippet    = name + method.ParameterList.ToString(),
                Severity       = Severity.Low,
                Category       = "Reliability"
            };
        }
    }
}
