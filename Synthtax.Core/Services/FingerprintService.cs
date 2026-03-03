using System.Security.Cryptography;
using System.Text;

namespace Synthtax.Core.Services;

/// <summary>
/// Beräknar ett deterministiskt SHA-256-fingerprint för en kodissue.
///
/// <para>Fingerprintet identifierar en issue unikt inom ett projekt och används för
/// att avgöra om en issue från en ny analyskörning redan finns i backloggen
/// (idempotent upsert). Det unika indexet (ProjectId, Fingerprint) i databasen
/// garanterar att inga dubletter kan skapas.</para>
///
/// <para><b>Normaliseringsregler</b> (viktigt för stabila fingerprints):
/// <list type="bullet">
///   <item>Filsökvägar normaliseras till forward-slash och görs relativa till projekt-roten.</item>
///   <item>Code-snippets trunkeras till 64 tecken och whitespace kollapsas.</item>
///   <item>Separatorn '|' används mellan komponenter — tecken som inte får förekomma i RuleId eller sökvägar.</item>
/// </list>
/// </para>
/// </summary>
public static class FingerprintService
{
    private const int SnippetMaxLength = 64;

    /// <summary>
    /// Beräknar fingerprint för en issue.
    /// </summary>
    /// <param name="ruleId">T.ex. "CA001", "JAVA003".</param>
    /// <param name="filePath">Absolut eller relativ sökväg. Normaliseras internt.</param>
    /// <param name="lineNumber">Radnummer (1-baserat).</param>
    /// <param name="codeSnippet">Kodsnippet. Null ger stabilt fingerprint utan snippet-komponent.</param>
    /// <param name="projectRootPath">Om angiven görs filePath relativ till denna rot.</param>
    public static string Compute(
        string  ruleId,
        string  filePath,
        int     lineNumber,
        string? codeSnippet    = null,
        string? projectRootPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath    = NormalizePath(filePath, projectRootPath);
        var normalizedSnippet = NormalizeSnippet(codeSnippet);

        // Format: "RULE|path/to/file.cs|42|snippet_first_64_chars"
        // Radnummer ingår för att skilja på samma regel på olika rader i samma fil.
        var raw = normalizedSnippet is null
            ? $"{ruleId}|{normalizedPath}|{lineNumber}"
            : $"{ruleId}|{normalizedPath}|{lineNumber}|{normalizedSnippet}";

        return ComputeSha256Hex(raw);
    }

    /// <summary>
    /// Beräknar fingerprint direkt från en sträng (t.ex. för tester eller batch-beräkning).
    /// </summary>
    public static string ComputeRaw(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        return ComputeSha256Hex(input);
    }

    // ── Privata hjälpmetoder ──────────────────────────────────────────────

    private static string NormalizePath(string filePath, string? rootPath)
    {
        // Normalisera separatorer
        var normalized = filePath.Replace('\\', '/');

        if (!string.IsNullOrEmpty(rootPath))
        {
            var root = rootPath.Replace('\\', '/').TrimEnd('/') + '/';
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[root.Length..];
        }

        return normalized.ToLowerInvariant();
    }

    private static string? NormalizeSnippet(string? snippet)
    {
        if (snippet is null) return null;

        // Kollaps whitespace och ta de första N tecknen
        var collapsed = System.Text.RegularExpressions.Regex
            .Replace(snippet.Trim(), @"\s+", " ");

        return collapsed.Length <= SnippetMaxLength
            ? collapsed
            : collapsed[..SnippetMaxLength];
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash  = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
