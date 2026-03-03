using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Rules;

public sealed class HardcodedCredentialRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC001";
    public string Name => "Hardcoded Credential";
    public bool IsEnabled => true;

    private static readonly HashSet<string> CredentialNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "secret", "apikey", "token", "connectionstring"
    };

    public IEnumerable<SecurityIssueDto> Analyze(SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var lit in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) continue;

            // Kolla variabelnamn vid tilldelning
            var parent = lit.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
            if (parent != null && CredentialNames.Any(k => parent.Identifier.Text.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                var span = lit.GetLocation().GetLineSpan();
                yield return new SecurityIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "HardcodedCredential",
                    Title = "Hårdkodad hemlighet",
                    Description = $"Variabeln '{parent.Identifier.Text}' verkar innehålla en hårdkodad sträng.",
                    Severity = Severity.High,
                    LineNumber = span.StartLinePosition.Line + 1,
                    Category = "Credentials"
                };
            }
        }
    }
}