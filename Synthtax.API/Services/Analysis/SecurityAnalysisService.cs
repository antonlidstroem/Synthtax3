using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;
using System.Text.RegularExpressions;

namespace Synthtax.API.Services.Analysis;

public class SecurityAnalysisService : ISecurityAnalysisService
{
    private readonly ILogger<SecurityAnalysisService> _logger;

    // Patterns for hardcoded credentials
    private static readonly Regex[] CredentialPatterns =
    {
        new(@"password\s*=\s*""[^""]{3,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"pwd\s*=\s*""[^""]{3,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"secret\s*=\s*""[^""]{3,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"apikey\s*=\s*""[^""]{3,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"api_key\s*=\s*""[^""]{3,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"connectionstring\s*=\s*""[^""]{10,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"token\s*=\s*""[^""]{8,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"private_key\s*=\s*""[^""]{5,}""", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    // Names that suggest SQL string concatenation risk
    private static readonly HashSet<string> SqlVariableHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "query", "sql", "commandText", "sqlQuery", "sqlCommand", "queryString", "statement"
    };

    public SecurityAnalysisService(ILogger<SecurityAnalysisService> logger)
    {
        _logger = logger;
    }

    public async Task<SecurityAnalysisResultDto> AnalyzeSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var result = new SecurityAnalysisResultDto { SolutionPath = solutionPath };

        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);

            using (workspace)
            {
                foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var root = await doc.GetSyntaxRootAsync(cancellationToken);
                    var model = await doc.GetSemanticModelAsync(cancellationToken);
                    if (root is null) continue;

                    var filePath = doc.FilePath ?? doc.Name;

                    result.HardcodedCredentials.AddRange(
                        FindHardcodedCredentialsInTree(root, filePath));
                    result.SqlInjectionRisks.AddRange(
                        FindSqlInjectionInTree(root, model, filePath));
                    result.InsecureRandomUsage.AddRange(
                        FindInsecureRandomInTree(root, model, filePath));
                    result.MissingCancellationTokens.AddRange(
                        FindMissingCancellationTokensInTree(root, model, filePath));
                }
            }

            result.AllIssues.AddRange(result.HardcodedCredentials);
            result.AllIssues.AddRange(result.SqlInjectionRisks);
            result.AllIssues.AddRange(result.InsecureRandomUsage);
            result.AllIssues.AddRange(result.MissingCancellationTokens);

            result.TotalIssues = result.AllIssues.Count;
            result.CriticalCount = result.AllIssues.Count(i => i.Severity == Severity.Critical);
            result.HighCount = result.AllIssues.Count(i => i.Severity == Severity.High);
            result.MediumCount = result.AllIssues.Count(i => i.Severity == Severity.Medium);
            result.LowCount = result.AllIssues.Count(i => i.Severity == Severity.Low);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Security analysis error for {Path}", solutionPath);
            result.Errors.Add($"Security analysis error: {ex.Message}");
        }

        return result;
    }

    public async Task<List<SecurityIssueDto>> FindHardcodedCredentialsAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<SecurityIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                if (root is null) continue;
                results.AddRange(FindHardcodedCredentialsInTree(root, doc.FilePath ?? doc.Name));
            }
        }
        return results;
    }

    public async Task<List<SecurityIssueDto>> FindSqlInjectionRisksAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<SecurityIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                var model = await doc.GetSemanticModelAsync(cancellationToken);
                if (root is null) continue;
                results.AddRange(FindSqlInjectionInTree(root, model, doc.FilePath ?? doc.Name));
            }
        }
        return results;
    }

    public async Task<List<SecurityIssueDto>> FindInsecureRandomUsageAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<SecurityIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                var model = await doc.GetSemanticModelAsync(cancellationToken);
                if (root is null) continue;
                results.AddRange(FindInsecureRandomInTree(root, model, doc.FilePath ?? doc.Name));
            }
        }
        return results;
    }

    public async Task<List<SecurityIssueDto>> FindMissingCancellationTokensAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
            solutionPath, _logger, cancellationToken);

        var results = new List<SecurityIssueDto>();
        using (workspace)
        {
            foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
            {
                var root = await doc.GetSyntaxRootAsync(cancellationToken);
                var model = await doc.GetSemanticModelAsync(cancellationToken);
                if (root is null) continue;
                results.AddRange(FindMissingCancellationTokensInTree(root, model, doc.FilePath ?? doc.Name));
            }
        }
        return results;
    }

    // ── Private Analysis Helpers ──────────────────────────────────────────────

    private static List<SecurityIssueDto> FindHardcodedCredentialsInTree(
        SyntaxNode root, string filePath)
    {
        var issues = new List<SecurityIssueDto>();
        var fileName = Path.GetFileName(filePath);
        var text = root.ToFullString();
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var pattern in CredentialPatterns)
            {
                if (pattern.IsMatch(line))
                {
                    // Skip if it looks like a placeholder or config reference
                    if (line.Contains("TODO") || line.Contains("CHANGE_ME") ||
                        line.Contains("configuration[") || line.Contains("config[") ||
                        line.Contains("Environment.GetEnvironmentVariable"))
                        continue;

                    issues.Add(new SecurityIssueDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        IssueType = "HardcodedCredential",
                        Title = "Hardcoded Credential Detected",
                        Description = "A literal string value that resembles a credential was found.",
                        Recommendation = "Move secrets to environment variables, Azure Key Vault, or appsettings with Secret Manager.",
                        LineNumber = i + 1,
                        CodeSnippet = line.Trim(),
                        Severity = Severity.High,
                        Category = "Credentials"
                    });
                    break;
                }
            }
        }

        return issues;
    }

    private static List<SecurityIssueDto> FindSqlInjectionInTree(
        SyntaxNode root, SemanticModel? model, string filePath)
    {
        var issues = new List<SecurityIssueDto>();
        var fileName = Path.GetFileName(filePath);

        // Look for string concatenation or interpolation assigned to SQL-hinted variables
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>();
        foreach (var assign in assignments)
        {
            if (assign.Left is not IdentifierNameSyntax id) continue;
            if (!SqlVariableHints.Contains(id.Identifier.Text)) continue;

            var isStringConcat = assign.Right is BinaryExpressionSyntax binary
                && binary.IsKind(SyntaxKind.AddExpression);
            var isInterpolated = assign.Right is InterpolatedStringExpressionSyntax;

            if (isStringConcat || isInterpolated)
            {
                var span = assign.GetLocation().GetLineSpan();
                issues.Add(new SecurityIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "SqlInjectionRisk",
                    Title = "Potential SQL Injection",
                    Description = $"Variable '{id.Identifier.Text}' is built using string concatenation or interpolation.",
                    Recommendation = "Use parameterized queries, stored procedures, or an ORM like Entity Framework Core.",
                    LineNumber = span.StartLinePosition.Line + 1,
                    CodeSnippet = assign.ToString().Trim()[..Math.Min(120, assign.ToString().Trim().Length)],
                    Severity = Severity.High,
                    Category = "Injection"
                });
            }
        }

        // Also catch: var declarations with SQL hints built via concatenation
        var localDeclarations = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();
        foreach (var decl in localDeclarations)
        {
            foreach (var variable in decl.Declaration.Variables)
            {
                if (!SqlVariableHints.Contains(variable.Identifier.Text)) continue;
                if (variable.Initializer?.Value is null) continue;

                var initValue = variable.Initializer.Value;
                var isStringConcat = initValue is BinaryExpressionSyntax bin
                    && bin.IsKind(SyntaxKind.AddExpression);
                var isInterpolated = initValue is InterpolatedStringExpressionSyntax;

                if (isStringConcat || isInterpolated)
                {
                    var span = decl.GetLocation().GetLineSpan();
                    issues.Add(new SecurityIssueDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        IssueType = "SqlInjectionRisk",
                        Title = "Potential SQL Injection",
                        Description = $"SQL variable '{variable.Identifier.Text}' constructed via string concatenation.",
                        Recommendation = "Use parameterized queries or EF Core to prevent SQL injection.",
                        LineNumber = span.StartLinePosition.Line + 1,
                        CodeSnippet = decl.ToString().Trim()[..Math.Min(120, decl.ToString().Trim().Length)],
                        Severity = Severity.High,
                        Category = "Injection"
                    });
                }
            }
        }

        return issues;
    }

    private static List<SecurityIssueDto> FindInsecureRandomInTree(
        SyntaxNode root, SemanticModel? model, string filePath)
    {
        var issues = new List<SecurityIssueDto>();
        var fileName = Path.GetFileName(filePath);

        // Find: new Random() or Random.Shared.Next() in security-sensitive contexts
        var objectCreations = root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(oc => oc.Type.ToString() == "Random");

        foreach (var creation in objectCreations)
        {
            // Check if used in context that looks security-sensitive
            var containingMethod = creation.Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();

            var methodName = containingMethod?.Identifier.Text ?? string.Empty;
            var isSensitiveContext = methodName.Contains("password", StringComparison.OrdinalIgnoreCase)
                || methodName.Contains("token", StringComparison.OrdinalIgnoreCase)
                || methodName.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || methodName.Contains("key", StringComparison.OrdinalIgnoreCase)
                || methodName.Contains("salt", StringComparison.OrdinalIgnoreCase)
                || methodName.Contains("guid", StringComparison.OrdinalIgnoreCase);

            var span = creation.GetLocation().GetLineSpan();
            var severity = isSensitiveContext ? Severity.High : Severity.Medium;

            issues.Add(new SecurityIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "InsecureRandom",
                Title = "Insecure Random Usage",
                Description = "System.Random is not cryptographically secure.",
                Recommendation = "Use System.Security.Cryptography.RandomNumberGenerator for security-sensitive random values.",
                LineNumber = span.StartLinePosition.Line + 1,
                CodeSnippet = creation.ToString().Trim(),
                Severity = severity,
                Category = "Cryptography"
            });
        }

        // Also catch: Random.Shared usage
        var memberAccesses = root.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(ma => ma.ToString().StartsWith("Random.Shared", StringComparison.OrdinalIgnoreCase));

        foreach (var ma in memberAccesses)
        {
            var span = ma.GetLocation().GetLineSpan();
            issues.Add(new SecurityIssueDto
            {
                FilePath = filePath,
                FileName = fileName,
                IssueType = "InsecureRandom",
                Title = "Insecure Random Usage (Random.Shared)",
                Description = "Random.Shared is not cryptographically secure.",
                Recommendation = "Use RandomNumberGenerator.GetInt32() or RandomNumberGenerator.Fill() instead.",
                LineNumber = span.StartLinePosition.Line + 1,
                CodeSnippet = ma.ToString().Trim(),
                Severity = Severity.Medium,
                Category = "Cryptography"
            });
        }

        return issues;
    }

    private static List<SecurityIssueDto> FindMissingCancellationTokensInTree(
        SyntaxNode root, SemanticModel? model, string filePath)
    {
        var issues = new List<SecurityIssueDto>();
        var fileName = Path.GetFileName(filePath);

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)));

        foreach (var method in methods)
        {
            // Skip private methods and test methods
            var isPrivate = method.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword));
            var name = method.Identifier.Text;
            if (name.StartsWith("Test", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("Async_Should", StringComparison.OrdinalIgnoreCase))
                continue;

            var hasCancellationToken = method.ParameterList.Parameters
                .Any(p => p.Type?.ToString()
                    .Contains("CancellationToken", StringComparison.OrdinalIgnoreCase) == true);

            if (!hasCancellationToken)
            {
                var span = method.GetLocation().GetLineSpan();
                issues.Add(new SecurityIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "MissingCancellationToken",
                    Title = "Async Method Missing CancellationToken",
                    Description = $"Async method '{name}' does not accept a CancellationToken parameter.",
                    Recommendation = "Add CancellationToken cancellationToken = default as last parameter to support cooperative cancellation.",
                    LineNumber = span.StartLinePosition.Line + 1,
                    CodeSnippet = method.Identifier.Text + method.ParameterList.ToString(),
                    Severity = Severity.Low,
                    Category = "Reliability"
                });
            }
        }

        return issues;
    }
}
