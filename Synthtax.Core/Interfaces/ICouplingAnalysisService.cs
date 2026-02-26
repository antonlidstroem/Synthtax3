using Synthtax.Core.DTOs;
namespace Synthtax.Core.Interfaces;

public interface ICouplingAnalysisService
{
    Task<CouplingAnalysisResultDto> AnalyzeSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);
}
