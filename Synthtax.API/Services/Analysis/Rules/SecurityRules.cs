using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis.Rules;

// ─────────────────────────────────────────────────────────────────────────────
// HardcodedCredential — uses the SyntaxTree instead of Regex so we get
// accurate line numbers and avoid false positives inside comments.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class HardcodedCredentialRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC001";
    public string Name => "Hardcoded Credential";
    public bool IsEnabled => true;

    private static readonly HashSet<string> CredentialPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password","pwd","secret","apikey","api_key","token","connectionstring",
        "private_key","accesskey","access_key","clientsecret","client_secret",
    };

    public IEnumerable<SecurityIssueDto> Analyze(
        SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);

        // ── Assignment expressions: password = "abc" ──────────────────────────
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

        // ── Variable declarations: var password = "abc" ───────────────────────
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

        // ── Named argument: new Foo(password: "abc") ─────────────────────────
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
        val.Contains("TODO", StringComparison.OrdinalIgnoreCase) ||
        val.Equals("***", StringComparison.Ordinal);

    private static SecurityIssueDto MakeIssue(
        string fileName, string filePath, int line, string snippet, string propName) =>
        new()
        {
            FilePath = filePath,
            FileName = fileName,
            IssueType = "HardcodedCredential",
            Title = "Hardcoded Credential Detected",
            Description = $"Literal value assigned to credential-sensitive identifier '{propName}'.",
            Recommendation = "Move secrets to environment variables, Azure Key Vault, or Secret Manager.",
            LineNumber = line,
            CodeSnippet = snippet.Length > 120 ? snippet[..120] : snippet,
            Severity = Severity.High,
            Category = "Credentials"
        };
}

// ─────────────────────────────────────────────────────────────────────────────
// SqlInjection — SEMANTIC version
//
// Old approach: checked variable name + string-concat syntactically → many FPs
// New approach: uses data-flow / symbol information to check whether string-
//   building expressions include non-constant sub-expressions (taint sources).
//   Specifically, any SQL-variable initialisation or assignment is flagged only
//   when REAL runtime values (parameters, fields, method calls) flow into it.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class SqlInjectionRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC002";
    public string Name => "SQL Injection Risk";
    public bool IsEnabled => true;

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

        // Check both declarations and assignments for SQL-named variables
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
                        rhs = init;
                        name = v.Identifier.Text;
                        line = v.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        snippet = decl.ToString().Trim();
                    }
                    break;

                case AssignmentExpressionSyntax assign
                    when assign.Left is IdentifierNameSyntax assignId &&
                         SqlVariableHints.Contains(assignId.Identifier.Text):
                    rhs = assign.Right;
                    name = assignId.Identifier.Text;
                    line = assign.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    snippet = assign.ToString().Trim();
                    break;
            }

            if (rhs is null || name is null) continue;

            // ── SEMANTIC CHECK: is the RHS tainted? ───────────────────────────
            if (!ContainsTaintedExpression(rhs, model))
                continue;

            yield return new SecurityIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "SqlInjectionRisk",
                Title = "Potential SQL Injection",
                Description = $"SQL variable '{name}' is constructed from non-constant values.",
                Recommendation = "Use parameterized queries, stored procedures, or EF Core to prevent SQL injection.",
                LineNumber = line,
                CodeSnippet = snippet.Length > 120 ? snippet[..120] : snippet,
                Severity = Severity.High,
                Category = "Injection"
            };
        }
    }

    /// <summary>
    /// Returns true if the expression contains at least one sub-expression
    /// that is NOT a compile-time constant.
    ///
    /// We walk the tree looking for identifiers/invocations that refer to
    /// runtime values. If the semantic model is unavailable we fall back to
    /// the syntactic heuristic (string concat or interpolation present).
    /// </summary>
    private static bool ContainsTaintedExpression(ExpressionSyntax expr, SemanticModel? model)
    {
        if (model is null)
        {
            // Fallback: syntactic check — same as old code
            return expr is BinaryExpressionSyntax bin && bin.IsKind(SyntaxKind.AddExpression)
                || expr is InterpolatedStringExpressionSyntax;
        }

        // Only care about string-building expressions
        if (expr is not (BinaryExpressionSyntax or InterpolatedStringExpressionSyntax))
            return false;

        foreach (var descendant in expr.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            // Compile-time constant → safe
            var constant = model.GetConstantValue(descendant);
            if (constant.HasValue) continue;

            // Literal → safe
            if (descendant is LiteralExpressionSyntax) continue;

            // Identifier or invocation that has a non-null type → tainted
            var typeInfo = model.GetTypeInfo(descendant);
            if (typeInfo.Type is not null &&
                typeInfo.Type.SpecialType == SpecialType.System_String)
            {
                // Check if it's a parameter — direct taint source
                if (descendant is IdentifierNameSyntax idSyn)
                {
                    var sym = model.GetSymbolInfo(idSyn).Symbol;
                    if (sym is IParameterSymbol or IFieldSymbol or IPropertySymbol)
                        return true;
                    // Local variable: flag it — we don't know its source here
                    if (sym is ILocalSymbol)
                        return true;
                }
                if (descendant is InvocationExpressionSyntax)
                    return true;
                if (descendant is MemberAccessExpressionSyntax)
                    return true;
            }
        }
        return false;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// InsecureRandom — improved
//
// Old: only checked new Random() and Random.Shared
// New: also checks Random.Next/NextDouble/NextBytes member access, tracks
//      context (security-sensitive methods, token/password generation),
//      and checks for seeded randoms which are deterministic.
// ─────────────────────────────────────────────────────────────────────────────

public sealed class InsecureRandomRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC003";
    public string Name => "Insecure Random Usage";
    public bool IsEnabled => true;

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

        // ── new Random(...) ───────────────────────────────────────────────────
        foreach (var oc in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!IsRandomType(oc.Type.ToString())) continue;

            var severity = GetSeverity(oc, model);
            var seeded = oc.ArgumentList?.Arguments.Count > 0;

            var span = oc.GetLocation().GetLineSpan();
            yield return new SecurityIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "InsecureRandom",
                Title = seeded ? "Seeded (Deterministic) Random Usage"
                                        : "Insecure Random Usage",
                Description = seeded
                    ? "A seeded System.Random produces deterministic sequences — never use for security."
                    : "System.Random is NOT cryptographically secure.",
                Recommendation = "Use System.Security.Cryptography.RandomNumberGenerator for any security-sensitive context.",
                LineNumber = span.StartLinePosition.Line + 1,
                CodeSnippet = oc.ToString().Trim(),
                Severity = severity,
                Category = "Cryptography"
            };
        }

        // ── Random.Shared.Next() / Random.Shared.NextBytes() etc. ────────────
        foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            ct.ThrowIfCancellationRequested();
            if (!ma.ToString().StartsWith("Random.Shared", StringComparison.OrdinalIgnoreCase))
                continue;

            // De-duplicate: only report at the Random.Shared level, not sub-accesses
            if (ma.Parent is MemberAccessExpressionSyntax) continue;

            var severity = GetSeverity(ma, model);
            var span = ma.GetLocation().GetLineSpan();
            yield return new SecurityIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "InsecureRandom",
                Title = "Insecure Random.Shared Usage",
                Description = "Random.Shared is NOT cryptographically secure.",
                Recommendation = "Use RandomNumberGenerator.GetInt32() or RandomNumberGenerator.Fill() instead.",
                LineNumber = span.StartLinePosition.Line + 1,
                CodeSnippet = ma.ToString().Trim(),
                Severity = severity,
                Category = "Cryptography"
            };
        }
    }

    private static bool IsRandomType(string typeName) =>
        typeName is "Random" or "System.Random";

    private static Severity GetSeverity(SyntaxNode node, SemanticModel? model)
    {
        // Walk up to the containing method and check its name + parameter names
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method is null) return Severity.Medium;

        var name = method.Identifier.Text;
        if (SecurityContextKeywords.Any(k =>
                name.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return Severity.High;

        // Also check local variable names that the random is assigned to
        var parent = node.Parent;
        if (parent is EqualsValueClauseSyntax evc &&
            evc.Parent is VariableDeclaratorSyntax vd &&
            SecurityContextKeywords.Any(k =>
                vd.Identifier.Text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return Severity.High;

        return Severity.Medium;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MissingCancellationToken (moved from SecurityAnalysisService for symmetry)
// ─────────────────────────────────────────────────────────────────────────────

public sealed class MissingCancellationTokenRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC004";
    public string Name => "Missing CancellationToken";
    public bool IsEnabled => true;

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
                name.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Async_Should", StringComparison.OrdinalIgnoreCase))
                continue;

            bool hasCt = method.ParameterList.Parameters.Any(p =>
                p.Type?.ToString()
                 .Contains("CancellationToken", StringComparison.OrdinalIgnoreCase) == true);

            if (!hasCt)
            {
                var span = method.GetLocation().GetLineSpan();
                yield return new SecurityIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "MissingCancellationToken",
                    Title = "Async Method Missing CancellationToken",
                    Description = $"Async method '{name}' does not accept a CancellationToken parameter.",
                    Recommendation = "Add CancellationToken cancellationToken = default as last parameter.",
                    LineNumber = span.StartLinePosition.Line + 1,
                    CodeSnippet = name + method.ParameterList.ToString(),
                    Severity = Severity.Low,
                    Category = "Reliability"
                };
            }
        }
    }
}
