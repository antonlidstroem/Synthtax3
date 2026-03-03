using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Rules;

public sealed class SqlInjectionRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC002";
    public string Name => "SQL Injection Risk";
    public bool IsEnabled => true;

    public IEnumerable<SecurityIssueDto> Analyze(SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var str in root.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        {
            // Enkel heuristik: om strängen innehåller SQL-kommandon och variabler
            var text = str.ToString().ToLower();
            if (text.Contains("select ") || text.Contains("insert ") || text.Contains("update "))
            {
                var span = str.GetLocation().GetLineSpan();
                yield return new SecurityIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "SqlInjection",
                    Title = "Potentiell SQL Injection",
                    Description = "SQL-fråga byggs med stränginterpolering.",
                    Severity = Severity.High,
                    LineNumber = span.StartLinePosition.Line + 1,
                    Category = "Injection"
                };
            }
        }
    }
}