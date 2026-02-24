using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// Hjälpklass för att ladda Solution/Project via MSBuildWorkspace.
/// Alla analysservices delar denna logik.
/// </summary>
public static class RoslynWorkspaceHelper
{
    /// <summary>
    /// Laddar en Solution från disk. Kastar InvalidOperationException om den inte hittas.
    /// </summary>
    public static async Task<(MSBuildWorkspace workspace, Solution solution)> LoadSolutionAsync(
        string solutionPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var workspace = MSBuildWorkspace.Create();

        workspace.WorkspaceFailed += (_, e) =>
            logger.LogWarning("Workspace diagnostic [{Kind}]: {Message}", e.Diagnostic.Kind, e.Diagnostic.Message);

        logger.LogInformation("Loading solution: {Path}", solutionPath);
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        logger.LogInformation("Solution loaded with {Count} project(s).", solution.Projects.Count());

        return (workspace, solution);
    }

    /// <summary>
    /// Laddar ett enskilt projekt.
    /// </summary>
    public static async Task<(MSBuildWorkspace workspace, Project project)> LoadProjectAsync(
        string projectPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        var workspace = MSBuildWorkspace.Create();

        workspace.WorkspaceFailed += (_, e) =>
            logger.LogWarning("Workspace diagnostic [{Kind}]: {Message}", e.Diagnostic.Kind, e.Diagnostic.Message);

        logger.LogInformation("Loading project: {Path}", projectPath);
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);

        return (workspace, project);
    }

    /// <summary>
    /// Hämtar alla C#-dokument från en solution (exkluderar generated files).
    /// </summary>
    public static IEnumerable<Document> GetCSharpDocuments(Solution solution)
        => solution.Projects
            .SelectMany(p => p.Documents)
            .Where(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     && !d.FilePath!.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !d.FilePath.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}")
                     && !d.Name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                     && !d.Name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Hämtar alla C#-dokument från ett projekt.
    /// </summary>
    public static IEnumerable<Document> GetCSharpDocuments(Project project)
        => project.Documents
            .Where(d => d.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                     && !d.FilePath!.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !d.Name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase));
}
