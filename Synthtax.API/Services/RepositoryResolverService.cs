using LibGit2Sharp;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services;

public sealed class RepositoryResolverService : IRepositoryResolver, IDisposable
{
    private readonly ILogger<RepositoryResolverService> _logger;
    private readonly string _cloneBaseDir;
    private readonly List<string> _tempDirsToClean = new();
    private bool _disposed;

    public RepositoryResolverService(ILogger<RepositoryResolverService> logger)
    {
        _logger = logger;
        _cloneBaseDir = Path.Combine(Path.GetTempPath(), "synthtax-clones");
        Directory.CreateDirectory(_cloneBaseDir);
    }

    /// <summary>
    /// Resolves a path or URL to a .sln file path.
    /// Accepts:
    ///   • A local path to a .sln file directly
    ///   • A local path to a folder that contains a .sln (searched recursively)
    ///   • A remote git URL – the repo is cloned and the .sln is located automatically
    /// The caller never needs to know whether input was a file, directory, or URL.
    /// </summary>
    public async Task<ResolvedPath> ResolveAsync(
        string? pathOrUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return ResolvedPath.Failure("Sökväg eller URL saknas.");

        // ── Remote URL ──────────────────────────────────────────────────────
        if (IRepositoryResolver.IsRemoteUrl(pathOrUrl))
        {
            var cloneResult = await CloneAsync(pathOrUrl, ct);
            if (!cloneResult.Success) return cloneResult;

            var sln = FindSlnInDirectory(cloneResult.LocalPath!);
            if (sln is null)
            {
                Cleanup(cloneResult.CloneDir);
                return ResolvedPath.Failure(
                    "Inga .sln-filer hittades i repositoryt. " +
                    "Kontrollera att det är ett C#/.NET-projekt.");
            }

            _logger.LogInformation("Hittade solution i klonat repo: {Sln}", sln);
            return ResolvedPath.Ok(sln, isClone: true,
                cloneDir: cloneResult.CloneDir,
                url: cloneResult.OriginalUrl);
        }

        // ── Local path pointing directly to a .sln file ─────────────────────
        var normalized = pathOrUrl.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (File.Exists(normalized) &&
            normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedPath.Ok(normalized, isClone: false);
        }

        // ── Local directory ──────────────────────────────────────────────────
        // Also handles the case where the user passed a path to a .sln file that
        // somehow got a trailing separator.
        if (Directory.Exists(normalized))
        {
            var found = FindSlnInDirectory(normalized);
            if (found is null)
                return ResolvedPath.Failure(
                    $"Ingen .sln-fil hittades i mappen: {normalized}");

            _logger.LogInformation("Hittade solution i mapp: {Sln}", found);
            return ResolvedPath.Ok(found, isClone: false);
        }

        // ── Nothing matched ──────────────────────────────────────────────────
        // Give a helpful error that distinguishes "looks like a file path" from
        // "looks like a directory path" so the WPF client can show a better message.
        var looksLikeFile = Path.GetExtension(normalized).Length > 0;
        return looksLikeFile
            ? ResolvedPath.Failure($"Filen hittades inte: {normalized}")
            : ResolvedPath.Failure($"Mappen hittades inte: {normalized}");
    }

    /// <summary>
    /// Resolves a path or URL to a local directory (not a .sln file).
    /// Used by Git analysis which operates on the repo root, not a solution file.
    /// </summary>
    public async Task<ResolvedPath> ResolveDirectoryAsync(
        string? pathOrUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return ResolvedPath.Failure("Sökväg eller URL saknas.");

        if (!IRepositoryResolver.IsRemoteUrl(pathOrUrl))
        {
            var normalized = pathOrUrl.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (Directory.Exists(normalized))
                return ResolvedPath.Ok(normalized, isClone: false);

            // Accept a file path – return its parent directory
            if (File.Exists(normalized))
                return ResolvedPath.Ok(Path.GetDirectoryName(normalized)!, isClone: false);

            return ResolvedPath.Failure($"Sökvägen hittades inte: {normalized}");
        }

        return await CloneAsync(pathOrUrl, ct);
    }

    public void Cleanup(string? cloneDir)
    {
        if (cloneDir is null) return;
        TryDeleteDir(cloneDir);
        _tempDirsToClean.Remove(cloneDir);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<ResolvedPath> CloneAsync(string url, CancellationToken ct)
    {
        var cleanUrl = NormalizeUrl(url);
        var repoName = ExtractRepoName(cleanUrl);
        var cloneDir = Path.Combine(_cloneBaseDir, $"{repoName}_{Guid.NewGuid():N}");

        _logger.LogInformation("Klonar {Url} → {Dir}", cleanUrl, cloneDir);
        try
        {
            await Task.Run(() =>
                Repository.Clone(cleanUrl, cloneDir, new CloneOptions
                {
                    RecurseSubmodules = false,
                    Checkout = true
                }), ct);

            _tempDirsToClean.Add(cloneDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kloning misslyckades för {Url}", cleanUrl);
            TryDeleteDir(cloneDir);
            return ResolvedPath.Failure(
                $"Kunde inte klona repositoryt: {ex.Message}. " +
                "Kontrollera att URL:en är korrekt och att repositoryt är publikt.");
        }

        return ResolvedPath.Ok(cloneDir, isClone: true, cloneDir: cloneDir, url: cleanUrl);
    }

    private static string? FindSlnInDirectory(string dir)
        => Directory
            .GetFiles(dir, "*.sln", SearchOption.AllDirectories)
            .OrderBy(f => f.Length)   // prefer shallowest / shortest path
            .FirstOrDefault();

    /// <summary>
    /// Normalises a git remote URL:
    ///   – Strips trailing slashes
    ///   – Appends .git if missing (GitHub, Azure DevOps, GitLab all accept this)
    ///   – Does NOT append .git to URLs that already contain .git anywhere in the path
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');

        // Don't double-append .git
        if (!url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url += ".git";

        return url;
    }

    private static string ExtractRepoName(string url)
    {
        var parts = url.TrimEnd('/').Split('/', '\\');
        var name = parts.LastOrDefault() ?? "repo";
        return name.Replace(".git", "", StringComparison.OrdinalIgnoreCase)
                   .Replace(" ", "_");
    }

    private void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kunde inte ta bort {Dir}", dir);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var dir in _tempDirsToClean.ToList())
            TryDeleteDir(dir);
    }
}
