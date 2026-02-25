using LibGit2Sharp;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services;

/// <summary>
/// Resolves a user-supplied path or GitHub URL to a usable local path.
///
/// Usage pattern in controllers:
///   var resolved = await _resolver.ResolveAsync(input, ct);          // for .sln-based analysis
///   var resolved = await _resolver.ResolveDirectoryAsync(input, ct); // for git-based analysis
///   if (!resolved.Success) return BadRequest(resolved.ErrorMessage);
///   try { ... use resolved.LocalPath ... }
///   finally { if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir); }
/// </summary>
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

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<ResolvedPath> ResolveAsync(
        string? pathOrUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return ResolvedPath.Failure("Sökväg eller URL saknas.");

        if (!IRepositoryResolver.IsRemoteUrl(pathOrUrl))
        {
            // Local path – accept both a direct .sln file and a directory containing one
            if (File.Exists(pathOrUrl) &&
                pathOrUrl.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                return ResolvedPath.Ok(pathOrUrl, isClone: false);

            if (Directory.Exists(pathOrUrl))
            {
                var found = FindSlnInDirectory(pathOrUrl);
                if (found is null)
                    return ResolvedPath.Failure(
                        $"Ingen .sln-fil hittades i mappen: {pathOrUrl}");
                return ResolvedPath.Ok(found, isClone: false);
            }

            return ResolvedPath.Failure($"Sökvägen hittades inte: {pathOrUrl}");
        }

        // Remote URL – clone then find .sln
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

        _logger.LogInformation("Hittade solution: {Sln}", sln);
        return ResolvedPath.Ok(sln, isClone: true,
            cloneDir: cloneResult.CloneDir,
            url: cloneResult.OriginalUrl);
    }

    /// <inheritdoc/>
    public async Task<ResolvedPath> ResolveDirectoryAsync(
        string? pathOrUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return ResolvedPath.Failure("Sökväg eller URL saknas.");

        if (!IRepositoryResolver.IsRemoteUrl(pathOrUrl))
        {
            // Accept a directory directly
            if (Directory.Exists(pathOrUrl))
                return ResolvedPath.Ok(pathOrUrl, isClone: false);

            // Accept a file path – use its containing directory
            if (File.Exists(pathOrUrl))
                return ResolvedPath.Ok(
                    Path.GetDirectoryName(pathOrUrl)!, isClone: false);

            return ResolvedPath.Failure($"Sökvägen hittades inte: {pathOrUrl}");
        }

        // Remote URL – clone and return the directory
        return await CloneAsync(pathOrUrl, ct);
    }

    /// <inheritdoc/>
    public void Cleanup(string? cloneDir)
    {
        if (cloneDir is null) return;
        TryDeleteDir(cloneDir);
        _tempDirsToClean.Remove(cloneDir);
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

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

        return ResolvedPath.Ok(cloneDir, isClone: true,
            cloneDir: cloneDir, url: cleanUrl);
    }

    private static string? FindSlnInDirectory(string dir)
        => Directory
            .GetFiles(dir, "*.sln", SearchOption.AllDirectories)
            .OrderBy(f => f.Length)   // prefer top-level / shortest path
            .FirstOrDefault();

    private static string NormalizeUrl(string url)
    {
        url = url.Trim().TrimEnd('/');
        if (!url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            url += ".git";
        return url;
    }

    private static string ExtractRepoName(string url)
    {
        var parts = url.TrimEnd('/').Split('/', '\\');
        var name = parts.LastOrDefault() ?? "repo";
        return name.Replace(".git", "").Replace(" ", "_");
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
