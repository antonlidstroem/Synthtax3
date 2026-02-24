using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IStructureAnalysisService
{
    /// <summary>
    /// Bygger en trädstruktur av solution med alla namespace, klasser och members.
    /// </summary>
    Task<StructureAnalysisResultDto> AnalyzeSolutionStructureAsync(string solutionPath, CancellationToken cancellationToken = default);
}
