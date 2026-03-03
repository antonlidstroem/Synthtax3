using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Rules;

public sealed class InsecureRandomRule : IAnalysisRule<SecurityIssueDto>
{
    public string RuleId => "SEC003";
    public string Name => "Insecure Random";
    public bool IsEnabled => true;

    public IEnumerable<SecurityIssueDto> Analyze(SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct = default)
    {
        var fileName = Path.GetFileName(filePath);
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (creation.Type.ToString() == "Random")
            {
                var span = creation.GetLocation().GetLineSpan();
                yield return new SecurityIssueDto
                {
                    FilePath = filePath,
                    FileName = fileName,
                    IssueType = "InsecureRandom",
                    Title = "Olämplig slumptalsgenerator",
                    Description = "System.Random bör inte användas för säkerhetskritiska operationer.",
                    Severity = Severity.Medium,
                    LineNumber = span.StartLinePosition.Line + 1,
                    Category = "Cryptography"
                };
            }
        }
    }
}