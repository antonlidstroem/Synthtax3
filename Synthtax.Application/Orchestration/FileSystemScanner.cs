namespace Synthtax.Application.Orchestration;

// ═══════════════════════════════════════════════════════════════════════════
// IFileScanner
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Skannar ett projekts källfiler och returnerar dem för analys.
/// Separerad från orchestratorn för att möjliggöra testning utan filsystem.
/// </summary>
public interface IFileScanner
{
    /// <summary>
    /// Returnerar alla källkodsfiler i projektets rotkatalog som matchas av
    /// de registrerade plugins.
    /// </summary>
    /// <param name="projectRootPath">Absolut sökväg till projektroten.</param>
    /// <param name="supportedExtensions">Filändelser som ska inkluderas.</param>
    /// <param name="extensionFilter">Ytterligare filter (null = ingen ytterligare begränsning).</param>
    IAsyncEnumerable<SourceFile> ScanAsync(
        string              projectRootPath,
        IReadOnlySet<string> supportedExtensions,
        IReadOnlySet<string>? extensionFilter,
        CancellationToken   ct = default);
}

/// <summary>En källkodsfil som ska analyseras.</summary>
public sealed record SourceFile(
    /// <summary>Absolut sökväg — används av plugin för att läsa filen.</summary>
    string AbsolutePath,

    /// <summary>
    /// Relativ sökväg från projektroten — används i fingerprinting och UI.
    /// Forward-slash-separerad (plattformsneutral).
    /// </summary>
    string RelativePath,

    /// <summary>Filändelse inklusive punkt, t.ex. ".cs".</summary>
    string Extension
);

// ═══════════════════════════════════════════════════════════════════════════
// FileSystemScanner — produktionsimplementering
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Skannar filsystemet. Hoppar över kataloger som typiskt inte innehåller källkod.
/// </summary>
public sealed class FileSystemScanner : IFileScanner
{
    // Kataloger att alltid hoppa över
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea",
        "__pycache__", ".pytest_cache", ".mypy_cache", ".venv", "venv",
        "dist", "build", "out", "target", "coverage", ".gradle"
    };

    public async IAsyncEnumerable<SourceFile> ScanAsync(
        string               projectRootPath,
        IReadOnlySet<string> supportedExtensions,
        IReadOnlySet<string>? extensionFilter,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken    ct = default)
    {
        var root     = Path.GetFullPath(projectRootPath.TrimEnd(Path.DirectorySeparatorChar));
        var rootSpan = root.AsSpan();

        // Kombinera filter: bara extensions som är BÅDE supported och (om filter angett) i filter
        var effectiveExtensions = extensionFilter is null
            ? supportedExtensions
            : (IReadOnlySet<string>)supportedExtensions
                .Intersect(extensionFilter, StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var files = EnumerateFiles(root, effectiveExtensions);

        foreach (var absPath in files)
        {
            ct.ThrowIfCancellationRequested();

            var ext      = Path.GetExtension(absPath);
            var rel      = absPath.AsSpan()[rootSpan.Length..].TrimStart(Path.DirectorySeparatorChar);
            var relStr   = rel.ToString().Replace('\\', '/');

            yield return new SourceFile(absPath, relStr, ext);

            // Ge CPU tillbaka var 200:e fil för att inte blockera threadpoolen
            if (Environment.TickCount64 % 200 == 0)
                await Task.Yield();
        }
    }

    private static IEnumerable<string> EnumerateFiles(
        string root, IReadOnlySet<string> extensions)
    {
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var dir = queue.Dequeue();

            // Filer i aktuell katalog
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { continue; }

            foreach (var f in files)
                if (extensions.Contains(Path.GetExtension(f)))
                    yield return f;

            // Underkataloger — hoppa över ignorerade
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }

            foreach (var sub in subdirs)
                if (!IgnoredDirectories.Contains(Path.GetFileName(sub)))
                    queue.Enqueue(sub);
        }
    }
}
