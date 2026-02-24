using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IMetricsService
{
    /// <summary>
    /// Beräknar metrics (LOC, komplexitet, maintainability) för hela solution.
    /// </summary>
    Task<MetricsResultDto> AnalyzeSolutionMetricsAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Beräknar metrics för ett enskilt projekt.
    /// </summary>
    Task<MetricsResultDto> AnalyzeProjectMetricsAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Beräknar metrics för en enskild fil.
    /// </summary>
    Task<FileMetricsDto> AnalyzeFileMetricsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar trenddata baserat på Git-historik.
    /// </summary>
    Task<List<MetricsTrendPointDto>> GetMetricsTrendAsync(string solutionPath, int maxDataPoints = 30, CancellationToken cancellationToken = default);
}
