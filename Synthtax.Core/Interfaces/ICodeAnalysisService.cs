using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface ICodeAnalysisService
{
    /// <summary>
    /// Analyserar en .NET solution med Roslyn och returnerar kodproblem.
    /// </summary>
    Task<CodeAnalysisResultDto> AnalyzeSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyserar ett enskilt projekt.
    /// </summary>
    Task<CodeAnalysisResultDto> AnalyzeProjectAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter metoder som överstiger angiven radgräns.
    /// </summary>
    Task<List<CodeIssueDto>> FindLongMethodsAsync(string solutionPath, int maxLines = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter oanvända/döda variabler.
    /// </summary>
    Task<List<CodeIssueDto>> FindDeadVariablesAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter onödiga using-statements.
    /// </summary>
    Task<List<CodeIssueDto>> FindUnnecessaryUsingsAsync(string solutionPath, CancellationToken cancellationToken = default);
}
