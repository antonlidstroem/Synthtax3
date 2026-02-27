using Microsoft.CodeAnalysis;

namespace Synthtax.Core.Interfaces;

/// <summary>
/// A single, focused analysis rule.  Rules are stateless and reusable;
/// the engine decides parallelism and aggregation.
/// </summary>
public interface IAnalysisRule<TResult>
{
    string RuleId { get; }
    string Name { get; }
    bool IsEnabled { get; }

    IEnumerable<TResult> Analyze(
        SyntaxNode root,
        SemanticModel? model,
        string filePath,
        CancellationToken ct = default);
}
