namespace Synthtax.Core.Interfaces;

/// <summary>
/// Resolves a user-supplied path or GitHub URL to a local path usable by analysis services.
/// For solution-based analysis (Roslyn) use ResolveAsync → returns path to .sln file.
/// For directory-based analysis (Git) use ResolveDirectoryAsync → returns path to repo root.
/// </summary>
public interface IRepositoryResolver
{
    /// <summary>
    /// Resolves a local .sln path or a GitHub URL to a local .sln file path.
    /// Clones the repository if a remote URL is supplied and locates the .sln file.
    /// </summary>
    Task<ResolvedPath> ResolveAsync(string? pathOrUrl, CancellationToken ct = default);

    /// <summary>
    /// Resolves a local directory path or a GitHub URL to a local directory path.
    /// Used by Git analysis which needs the repo root, not a specific .sln file.
    /// </summary>
    Task<ResolvedPath> ResolveDirectoryAsync(string? pathOrUrl, CancellationToken ct = default);

    /// <summary>
    /// Deletes a temporary clone directory created during resolution.
    /// Always call this in a finally block after analysis is complete.
    /// </summary>
    void Cleanup(string? cloneDir);

    /// <summary>Returns true if the input looks like a remote git URL.</summary>
    static bool IsRemoteUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        return input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || input.StartsWith("git@", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Result of a repository resolution operation.
/// </summary>
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
