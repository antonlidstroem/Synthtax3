using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis;

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

public sealed class WorkspaceOptions
{
    /// <summary>Path segments that mark a file as generated/excluded.</summary>
    public string[] ExcludedPathSegments { get; set; } =
    {
        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}",
        $"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}",
        $"{Path.AltDirectorySeparatorChar}Generated{Path.AltDirectorySeparatorChar}",
    };

    /// <summary>File name suffixes that mark a file as generated.</summary>
    public string[] ExcludedFileSuffixes { get; set; } =
    {
        ".g.cs", ".g.i.cs", ".designer.cs",
        ".AssemblyAttributes.cs", ".AssemblyInfo.cs",
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Injectable, testable replacement for the old static RoslynWorkspaceHelper.
/// Fixes:
///   • !d.FilePath!.Contains(...) — was a null-forgiving lie; now we check properly.
///   • Generated-file exclusions are configurable, not hard-coded strings.
/// </summary>
public sealed class RoslynWorkspaceService : IRoslynWorkspaceService
{
    private readonly ILogger<RoslynWorkspaceService> _logger;
    private readonly WorkspaceOptions _options;

    public RoslynWorkspaceService(
        ILogger<RoslynWorkspaceService> logger,
        IOptions<WorkspaceOptions>? options = null)
    {
        _logger = logger;
        _options = options?.Value ?? new WorkspaceOptions();
    }

    // ── Load solution ─────────────────────────────────────────────────────────

    public async Task<(MSBuildWorkspace Workspace, Solution Solution)> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(solutionPath))
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");

        var workspace = MSBuildWorkspace.Create();
        AttachDiagnostics(workspace);

        _logger.LogInformation("Loading solution: {Path}", solutionPath);
        var solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        _logger.LogInformation("Solution loaded with {Count} project(s).", solution.Projects.Count());

        return (workspace, solution);
    }

    // ── Load project ──────────────────────────────────────────────────────────

    public async Task<(MSBuildWorkspace Workspace, Project Project)> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project file not found: {projectPath}");

        var workspace = MSBuildWorkspace.Create();
        AttachDiagnostics(workspace);

        _logger.LogInformation("Loading project: {Path}", projectPath);
        var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);

        return (workspace, project);
    }

    // ── Document filtering ────────────────────────────────────────────────────

    public IEnumerable<Document> GetCSharpDocuments(Solution solution) =>
        solution.Projects.SelectMany(GetCSharpDocuments);

    public IEnumerable<Document> GetCSharpDocuments(Project project) =>
        project.Documents.Where(IsAnalysable);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsAnalysable(Document doc)
    {
        // Filter by extension first (cheapest check)
        if (!doc.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        // Filter by file name suffix
        foreach (var suffix in _options.ExcludedFileSuffixes)
            if (doc.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

        // FilePath may be null for in-memory / embedded documents — keep those.
        var path = doc.FilePath;
        if (path is null) return true;

        // Filter by path segment — safe now that we know path is non-null
        foreach (var seg in _options.ExcludedPathSegments)
            if (path.Contains(seg, StringComparison.OrdinalIgnoreCase))
                return false;

        return true;
    }

    private void AttachDiagnostics(MSBuildWorkspace workspace) =>
        workspace.WorkspaceFailed += (_, e) =>
            _logger.LogWarning(
                "Workspace [{Kind}]: {Message}",
                e.Diagnostic.Kind,
                e.Diagnostic.Message);
}
