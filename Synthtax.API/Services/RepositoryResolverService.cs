using System.Collections.Concurrent;
using LibGit2Sharp;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services;

public sealed class RepositoryResolverService : IRepositoryResolver, IDisposable
{
    private readonly ILogger<RepositoryResolverService> _logger;
    private readonly string _cloneBaseDir;

    // ARCH-02 FIX: List<string> var inte thread-safe — ersatt med ConcurrentBag.
    private readonly ConcurrentBag<string> _tempDirsToClean = new();

    private bool _disposed;

    public RepositoryResolverService(ILogger<RepositoryResolverService> logger)
    {
        _logger = logger;
        _cloneBaseDir = Path.Combine(Path.GetTempPath(), "synthtax-clones");
        Directory.CreateDirectory(_cloneBaseDir);
    }

    public async Task<ResolvedPath> ResolveAsync(
        string? pathOrUrl,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
            return ResolvedPath.Failure("Sökväg eller URL saknas.");

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

        var normalized = pathOrUrl.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // SEC-07 FIX: Validera att sökvägen inte innehåller path traversal (../).
        // GetFullPath löser upp alla relativa komponenter och vi kontrollerar sedan
        // att den resulterande sökvägen faktiskt existerar och är läsbar.
        var sanitized = SanitizeLocalPath(normalized);
        if (sanitized is null)
            return ResolvedPath.Failure(
                "Sökvägen innehåller ogiltiga komponenter (t.ex. ../) och nekas av säkerhetsskäl.");

        if (File.Exists(sanitized) &&
            sanitized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return ResolvedPath.Ok(sanitized, isClone: false);
        }

        if (Directory.Exists(sanitized))
        {
            var found = FindSlnInDirectory(sanitized);
            if (found is null)
                return ResolvedPath.Failure($"Ingen .sln-fil hittades i mappen: {sanitized}");

            _logger.LogInformation("Hittade solution i mapp: {Sln}", found);
            return ResolvedPath.Ok(found, isClone: false);
        }

        var looksLikeFile = Path.GetExtension(sanitized).Length > 0;
        return looksLikeFile
            ? ResolvedPath.Failure($"Filen hittades inte: {sanitized}")
            : ResolvedPath.Failure($"Mappen hittades inte: {sanitized}");
    }

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

            // SEC-07 FIX: samma skydd som i ResolveAsync.
            var sanitized = SanitizeLocalPath(normalized);
            if (sanitized is null)
                return ResolvedPath.Failure(
                    "Sökvägen innehåller ogiltiga komponenter och nekas av säkerhetsskäl.");

            if (Directory.Exists(sanitized))
                return ResolvedPath.Ok(sanitized, isClone: false);

            if (File.Exists(sanitized))
                return ResolvedPath.Ok(Path.GetDirectoryName(sanitized)!, isClone: false);

            return ResolvedPath.Failure($"Sökvägen hittades inte: {sanitized}");
        }

        return await CloneAsync(pathOrUrl, ct);
    }

    public void Cleanup(string? cloneDir)
    {
        if (cloneDir is null) return;
        TryDeleteDir(cloneDir);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// SEC-07 FIX: Löser upp sökvägen med Path.GetFullPath för att neutralisera
    /// path traversal-sekvenser (../, ..\). Returnerar null om sökvägen är ogiltig.
    /// </summary>
    private static string? SanitizeLocalPath(string raw)
    {
        try
        {
            var full = Path.GetFullPath(raw);
            // Tillåt inte sökvägar som börjar med /etc, /proc, /sys eller
            // windowsrotens system32-katalog etc. — utöka listan vid behov.
            if (IsSystemPath(full)) return null;
            return full;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSystemPath(string path)
    {
        // Blockera välkända känsliga kataloger på Linux/macOS
        var blocked = new[]
        {
            "/etc", "/proc", "/sys", "/boot", "/root",
            "/usr/sbin", "/usr/bin", "/bin", "/sbin"
        };
        return blocked.Any(b => path.StartsWith(b, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<ResolvedPath> CloneAsync(string url, CancellationToken ct)
    {
        var cleanUrl  = NormalizeUrl(url);
        var repoName  = ExtractRepoName(cleanUrl);
        var cloneDir  = Path.Combine(_cloneBaseDir, $"{repoName}_{Guid.NewGuid():N}");

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
            .OrderBy(f => f.Length)   // föredra grundaste / kortaste sökvägen
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
        var name  = parts.LastOrDefault() ?? "repo";
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
        foreach (var dir in _tempDirsToClean)
            TryDeleteDir(dir);
    }
}
