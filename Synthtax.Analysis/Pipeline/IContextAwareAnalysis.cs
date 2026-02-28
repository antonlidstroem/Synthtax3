using Microsoft.CodeAnalysis;
using Synthtax.Analysis.Workspace;

namespace Synthtax.Analysis.Pipeline;

/// <summary>
/// Marker interface for services that can accept a pre-built AnalysisContext
/// instead of loading the solution themselves. Implement this to participate
/// in the shared-context pipeline and avoid redundant IO.
/// </summary>
public interface IContextAwareAnalysis
{
    Task<object> AnalyzeAsync(AnalysisContext ctx, CancellationToken ct);
}
