using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

/// <summary>
/// Loads a solution ONCE and fans analysis out to every registered service in parallel.
/// Avoids the N × MSBuildWorkspace.Open() pattern.
/// </summary>
public interface ISolutionAnalysisPipeline
{
    Task<FullAnalysisResultDto> RunFullAnalysisAsync(
        string solutionPath,
        FullAnalysisOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class FullAnalysisOptions
{
    public bool IncludeMetrics { get; init; } = true;
    public bool IncludeSecurity { get; init; } = true;
    public bool IncludeCode { get; init; } = true;
    public bool IncludeCoupling { get; init; } = true;
    public bool IncludeAIDetection { get; init; } = true;
    public int MaxDegreeOfParallelism { get; init; } = 0; // 0 = Environment.ProcessorCount
}
