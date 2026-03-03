using Microsoft.CodeAnalysis;
using Synthtax.Core.Contracts;

namespace Synthtax.Analysis.Rules;

public interface ISynthtaxRule
{
    string RuleId { get; }
    IEnumerable<RawIssue> Analyze(SyntaxNode root, SemanticModel? model, string filePath, CancellationToken ct);
}