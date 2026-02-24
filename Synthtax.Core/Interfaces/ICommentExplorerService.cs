using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface ICommentExplorerService
{
    /// <summary>
    /// Extraherar alla kommentarer och regioner från en solution.
    /// </summary>
    Task<CommentExplorerResultDto> GetAllCommentsAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar enbart TODO/FIXME/HACK-kommentarer.
    /// </summary>
    Task<List<CommentDto>> GetTodoCommentsAsync(string solutionPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hämtar alla #region-definitioner.
    /// </summary>
    Task<List<RegionDto>> GetRegionsAsync(string solutionPath, CancellationToken cancellationToken = default);
}
