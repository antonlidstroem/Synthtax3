using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IMethodExplorerService
{
    /// <summary>
    /// Listar alla metoder i en solution med signatur, rad och komplexitet.
    /// </summary>
    Task<MethodExplorerResultDto> GetAllMethodsAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Söker efter metoder som matchar ett namnmönster.
    /// </summary>
    Task<List<MethodDto>> SearchMethodsAsync(string solutionPath, string searchPattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar metoder för en specifik klass.
    /// </summary>
    Task<List<MethodDto>> GetMethodsForClassAsync(string solutionPath, string className, CancellationToken cancellationToken = default);
}
