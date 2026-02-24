using LibGit2Sharp;

namespace Synthtax.API.Services;

/// <summary>
/// Löser en sökväg till en faktisk lokal .sln-fil.
/// Om indata är en GitHub/GitLab-URL klonas repositoryt till en tillfällig mapp.
/// </summary>
public sealed class RepositoryResolverService : IDisposable
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
    /// Om <paramref name="solutionPathOrUrl"/> ser ut som en URL (GitHub, GitLab, Bitbucket etc.)
    /// klonas repositoryt och sökvägen till den funna .sln-filen returneras.
    /// Annars returneras sökvägen oförändrad.
    /// </summary>
    public async Task<ResolvedPath> ResolveAsync(
        string? solutionPathOrUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(solutionPathOrUrl))
            return ResolvedPath.Failure("Sökväg eller URL saknas.");

        if (!IsRemoteUrl(solutionPathOrUrl))
        {
            // Lokal sökväg — validera att den finns
            if (!File.Exists(solutionPathOrUrl) && !Directory.Exists(solutionPathOrUrl))
                return ResolvedPath.Failure($"Sökvägen hittades inte: {solutionPathOrUrl}");

            return ResolvedPath.Ok(solutionPathOrUrl, isClone: false);
        }

        return await CloneAndFindSlnAsync(solutionPathOrUrl, ct);
    }

    // ── Clone logic ──────────────────────────────────────────────────────

    private async Task<ResolvedPath> CloneAndFindSlnAsync(
        string url, CancellationToken ct)
    {
        // Sanitize URL: strip .git suffix if present, strip query string
        var cleanUrl = CleanUrl(url);
        var repoName = ExtractRepoName(cleanUrl);
        var cloneDir = Path.Combine(_cloneBaseDir, $"{repoName}_{Guid.NewGuid():N}");

        _logger.LogInformation("Klonar {Url} → {Dir}", cleanUrl, cloneDir);

        try
        {
            var cloneOptions = new CloneOptions
            {
                RecurseSubmodules = false,
                Checkout = true,
            };

            // LibGit2Sharp Clone is synchronous; run on thread pool
            await Task.Run(() =>
                Repository.Clone(cleanUrl, cloneDir, cloneOptions), ct);

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

        // Find .sln file(s)
        var slnFiles = Directory.GetFiles(cloneDir, "*.sln", SearchOption.AllDirectories)
            .OrderBy(f => f.Length) // prefer top-level / shortest path
            .ToList();

        if (slnFiles.Count == 0)
        {
            TryDeleteDir(cloneDir);
            _tempDirsToClean.Remove(cloneDir);
            return ResolvedPath.Failure(
                "Inga .sln-filer hittades i repositoryt. " +
                "Kontrollera att det är ett C#/.NET-projekt.");
        }

        var chosen = slnFiles.First();
        _logger.LogInformation("Hittade solution: {Sln}", chosen);

        return ResolvedPath.Ok(chosen, isClone: true, cloneDir: cloneDir, url: cleanUrl);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    public static bool IsRemoteUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("git@", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanUrl(string url)
    {
        // Remove trailing slash and .git suffix for display; LibGit2Sharp handles both
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
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Kunde inte ta bort {Dir}", dir); }
    }

    // ── Cleanup ──────────────────────────────────────────────────────────

    public void Cleanup(string? cloneDir)
    {
        if (cloneDir is null) return;
        TryDeleteDir(cloneDir);
        _tempDirsToClean.Remove(cloneDir);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var dir in _tempDirsToClean.ToList())
            TryDeleteDir(dir);
    }
}

/// <summary>Result of path resolution (local or cloned).</summary>
public sealed record ResolvedPath(
    bool Success,
    string? LocalPath,
    string? ErrorMessage,
    bool IsClone,
    string? CloneDir,
    string? OriginalUrl)
{
    public static ResolvedPath Ok(
        string path, bool isClone, string? cloneDir = null, string? url = null)
        => new(true, path, null, isClone, cloneDir, url);

    public static ResolvedPath Failure(string error)
        => new(false, null, error, false, null, null);
}
