using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Workspace;

public sealed class WorkspaceOptions
{
    public string[] ExcludedPathSegments { get; set; } =
    {
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}Generated{Path.AltDirectorySeparatorChar}",
    };

    public string[] ExcludedFileSuffixes { get; set; } =
    {
        ".g.cs", ".g.i.cs", ".designer.cs",
        ".AssemblyAttributes.cs", ".AssemblyInfo.cs",
    };
}

public sealed class RoslynWorkspaceService : IRoslynWorkspaceService
{
    private readonly ILogger<RoslynWorkspaceService> _logger;
    private readonly WorkspaceOptions _options;

    public RoslynWorkspaceService(
        ILogger<RoslynWorkspaceService> logger,
        IOptions<WorkspaceOptions>? options = null)
    {
        _logger  = logger;
        _options = options?.Value ?? new WorkspaceOptions();
    }

    public async Task<(MSBuildWorkspace Workspace, Solution Solution)> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var workspace = MSBuildWorkspace.Create();
        AttachDiagnostics(workspace);

        _logger.LogInformation("Loading solution: {Path}", solutionPath);
        var solution = await workspace.OpenSolutionAsync(
            solutionPath, cancellationToken: cancellationToken);
        _logger.LogInformation("Solution loaded with {Count} project(s).",
            solution.Projects.Count());

        return (workspace, solution);
    }

    public async Task<(MSBuildWorkspace Workspace, Project Project)> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        var workspace = MSBuildWorkspace.Create();
        AttachDiagnostics(workspace);

        _logger.LogInformation("Loading project: {Path}", projectPath);
        var project = await workspace.OpenProjectAsync(
            projectPath, cancellationToken: cancellationToken);

        return (workspace, project);
    }

    public IEnumerable<Document> GetCSharpDocuments(Solution solution) =>
        solution.Projects.SelectMany(GetCSharpDocuments);

    public IEnumerable<Document> GetCSharpDocuments(Project project) =>
        project.Documents.Where(IsAnalysable);

    private bool IsAnalysable(Document doc)
    {
        if (!doc.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var suffix in _options.ExcludedFileSuffixes)
            if (doc.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

        var path = doc.FilePath;
        if (path is null) return true;

        foreach (var seg in _options.ExcludedPathSegments)
            if (path.Contains(seg, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    private void AttachDiagnostics(MSBuildWorkspace workspace) =>
        workspace.WorkspaceFailed += (_, e) =>
            _logger.LogWarning("Workspace [{Kind}]: {Message}",
                e.Diagnostic.Kind, e.Diagnostic.Message);
}
