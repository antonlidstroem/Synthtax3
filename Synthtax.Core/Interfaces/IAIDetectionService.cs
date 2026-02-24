using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IAIDetectionService
{
    /// <summary>
    /// Analyserar en solution för AI-genererade kodmönster (heuristik-baserat).
    /// </summary>
    Task<AIDetectionResultDto> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyserar en enskild fil.
    /// </summary>
    Task<AIDetectionFileResultDto> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyserar ett textstycke direkt (för inklistrad kod).
    /// </summary>
    Task<AIDetectionFileResultDto> AnalyzeCodeTextAsync(string code, string virtualFileName = "input.cs", CancellationToken cancellationToken = default);

    Task<AIDetectionFileResultDto> AnalyzeCodeAsync(string code, string fileName, CancellationToken cancellationToken);
}
