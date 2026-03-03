using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Rules;

public sealed class MissingCancellationTokenRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC004";
    public string Name => "Missing CancellationToken";
    public bool IsEnabled => true;

    public IEnumerable<SecurityIssueDto> Analyze(SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)))
            {
                bool hasToken = method.ParameterList.Parameters.Any(p => p.Type?.ToString().Contains("CancellationToken") == true);
                if (!hasToken)
                {
                    var span = method.Identifier.GetLocation().GetLineSpan();
                    yield return new SecurityIssueDto
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        IssueType = "Reliability",
                        Title = "Saknar CancellationToken",
                        Description = $"Async-metoden '{method.Identifier.Text}' bör acceptera en CancellationToken.",
                        Severity = Severity.Low,
                        LineNumber = span.StartLinePosition.Line + 1,
                        Category = "Reliability"
                    };
                }
            }
        }
    }
}