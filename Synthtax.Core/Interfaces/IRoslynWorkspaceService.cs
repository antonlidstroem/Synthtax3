using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
namespace Synthtax.Core.Interfaces;

public interface IRoslynWorkspaceService
{
    Task<(MSBuildWorkspace Workspace, Solution Solution)> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);
    Task<(MSBuildWorkspace Workspace, Project Project)> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default);
    IEnumerable<Document> GetCSharpDocuments(Solution solution);
    IEnumerable<Document> GetCSharpDocuments(Project project);
}
