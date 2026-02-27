using Synthtax.Core.DTOs;
namespace Synthtax.Core.Interfaces;

public interface IRefactoringService
{
    Task<RefactoringResultDto> SuggestRefactoringsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);
    Task<RefactoringResultDto> SuggestRefactoringsForCodeAsync(
        string code,
        string fileName = "input.cs",
        CancellationToken cancellationToken = default);
}
