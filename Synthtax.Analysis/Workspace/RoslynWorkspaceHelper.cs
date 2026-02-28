using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace Synthtax.Analysis.Workspace;

/// <summary>
/// Lightweight static helpers used by services that load their own workspace
/// (not sharing a pre-built AnalysisContext).
/// </summary>
public static class RoslynWorkspaceHelper
{
    public static async Task<(MSBuildWorkspace workspace, Solution solution)> LoadSolutionAsync(
        string solutionPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
            logger.LogWarning("Workspace diagnostic [{Kind}]: {Message}",
                e.Diagnostic.Kind, e.Diagnostic.Message);

        logger.LogInformation("Loading solution: {Path}", solutionPath);
        var solution = await workspace.OpenSolutionAsync(
            solutionPath, cancellationToken: cancellationToken);
        logger.LogInformation("Solution loaded with {Count} project(s).",
            solution.Projects.Count());

        return (workspace, solution);
    }

    public static async Task<(MSBuildWorkspace workspace, Project project)> LoadProjectAsync(
        string projectPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, e) =>
            logger.LogWarning("Workspace diagnostic [{Kind}]: {Message}",
                e.Diagnostic.Kind, e.Diagnostic.Message);

        logger.LogInformation("Loading project: {Path}", projectPath);
        var project = await workspace.OpenProjectAsync(
            projectPath, cancellationToken: cancellationToken);

        return (workspace, project);
    }

    public static IEnumerable<Document> GetCSharpDocuments(Solution solution)
        => solution.Projects.SelectMany(GetCSharpDocuments);

    public static IEnumerable<Document> GetCSharpDocuments(Project project)
        => project.Documents
            .Where(d =>
                d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && d.FilePath is not null
                && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}")
                && !d.Name.EndsWith(".g.cs",        StringComparison.OrdinalIgnoreCase)
                && !d.Name.EndsWith(".designer.cs",  StringComparison.OrdinalIgnoreCase));
}
