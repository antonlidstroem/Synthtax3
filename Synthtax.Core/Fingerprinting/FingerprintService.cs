using System.Security.Cryptography;
using System.Text;
using Synthtax.Core.Normalization;

namespace Synthtax.Core.Fingerprinting;

/// <summary>
/// Beräknar deterministiska SHA-256 fingerprints för kodissues.
///
/// <para><b>Hash-formel:</b>
/// <code>
///   pre_hash = PROJECT::{projectId::N}||RULE::{ruleId}||SCOPE::{scope}||SNIPPET::{normalized}
///   fingerprint = SHA256(pre_hash.ToUpperInvariant()).ToHexLower()
/// </code>
/// Alla strängar är ToUpperInvariant() — <b>Culture-Invariant Case Normalization</b> — och
/// separeras med <c>||</c> (dubbel pipe, inte enskild, för att undvika kollisioner
/// om t.ex. RuleId råkar innehålla <c>|</c>-tecknet).
/// </para>
///
/// <para><b>Normaliserings-pipeline för snippet</b> (i ordning):
/// <list type="number">
///   <item>Strip kommentarer beroende på filändelse (C-stil, hash-stil, SQL, HTML).</item>
///   <item>Strip triple-quote strings (Python docstrings, C# raw strings).</item>
///   <item>Kollaps all whitespace (tabs, newlines, multipla spaces) → ett space.</item>
///   <item>Trim leading/trailing whitespace.</item>
///   <item>ToUpperInvariant() — kultur-invariant.</item>
///   <item>Trunkering till 128 tecken.</item>
/// </list>
/// </para>
///
/// <para><b>Stabilitet:</b> Fingerprints förblir identiska om:
/// <list type="bullet">
///   <item>Kommentarer läggs till eller tas bort i snippetet.</item>
///   <item>Indentering ändras.</item>
///   <item>Case ändras (camelCase → PascalCase).</item>
///   <item>Raden med buggen förflyttas men koden är identisk (scope-baserat).</item>
/// </list>
/// Fingerprints ÄNDRAS om:
/// <list type="bullet">
///   <item>Metoden byter namn (scope-komponent).</item>
///   <item>Kodens faktiska innehåll ändras (snippet-komponent).</item>
///   <item>En annan regel flaggar samma kod (rule-komponent).</item>
///   <item>Koden flyttas till ett annat projekt (project-komponent).</item>
/// </list>
/// </para>
///
/// <para><b>ERSÄTTER</b> <c>Synthtax.Domain.Services.FingerprintService</c> från Fas 1
/// (som använde filsökväg + radnummer). Fas 2-versionen är semantiskt stabil.</para>
/// </summary>
public sealed class FingerprintService : IFingerprintService
{
    private static readonly NormalizationOptions FingerprintNormOptions =
        NormalizationOptions.ForFingerprinting;

    // Separator som är extremt osannolikt att förekomma i RuleId, scope eller snippet
    private const string Sep = "||";

    // ── IFingerprintService ────────────────────────────────────────────────

    /// <inheritdoc/>
    public string Compute(FingerprintInput input) =>
        ComputeWithDiagnostics(input).Hash;

    /// <inheritdoc/>
    public IReadOnlyList<string> ComputeBatch(IReadOnlyList<FingerprintInput> inputs)
    {
        // Allokera SHA256 en gång för hela batchen — undviker repeated disposal
        using var sha = SHA256.Create();
        var results = new string[inputs.Count];
        for (int i = 0; i < inputs.Count; i++)
            results[i] = ComputeCore(inputs[i], sha).Hash;
        return results;
    }

    /// <inheritdoc/>
    public (string Hash, string NormalizedSnippet, string PreHashKey) ComputeWithDiagnostics(
        FingerprintInput input)
    {
        using var sha = SHA256.Create();
        return ComputeCore(input, sha);
    }

    // ── Intern beräkning ───────────────────────────────────────────────────

    private static (string Hash, string NormalizedSnippet, string PreHashKey) ComputeCore(
        FingerprintInput input,
        SHA256 sha)
    {
        ValidateInput(input);

        // Steg 1: Normalisera snippet
        var normalizedSnippet = SnippetNormalizer.Normalize(
            input.RawSnippet,
            FingerprintNormOptions,
            commentStyle:  CommentStyle.Auto,
            fileExtension: input.FileExtension);

        // Steg 2: Normalisera scope-nyckel (Culture-Invariant Case — redan ToUpperInvariant i LogicalScope)
        var scopeKey = input.Scope.ToFingerprintKey();

        // Steg 3: Normalisera RuleId (ToUpperInvariant)
        var ruleKey = input.RuleId.Trim().ToUpperInvariant();

        // Steg 4: Sätt ihop pre-hash nyckel
        // Format: PROJECT::{guid-N}||RULE::{RULEID}||SCOPE::{SCOPE[KIND]}||SNIPPET::{NORMALIZED}
        var preHashKey = new StringBuilder(256)
            .Append("PROJECT::")
            .Append(input.ProjectId.ToString("N").ToUpperInvariant())
            .Append(Sep)
            .Append("RULE::")
            .Append(ruleKey)
            .Append(Sep)
            .Append("SCOPE::")
            .Append(scopeKey)
            .Append(Sep)
            .Append("SNIPPET::")
            .Append(normalizedSnippet)
            .ToString();

        // Steg 5: SHA-256 hash
        var bytes = Encoding.UTF8.GetBytes(preHashKey);
        var hash  = sha.ComputeHash(bytes);
        var hex   = ToHexLower(hash);

        return (hex, normalizedSnippet, preHashKey);
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private static void ValidateInput(FingerprintInput input)
    {
        if (input.ProjectId == Guid.Empty)
            throw new ArgumentException("ProjectId får inte vara Guid.Empty.", nameof(input));

        if (string.IsNullOrWhiteSpace(input.RuleId))
            throw new ArgumentException("RuleId får inte vara tom.", nameof(input));

        if (input.Scope is null)
            throw new ArgumentNullException(nameof(input), "Scope får inte vara null.");

        // Tom snippet är tillåtet (t.ex. en hel klass flaggas utan specifikt snippet)
        // men null kastas eftersom det indikerar ett programmeringsfel i plugin-koden.
        if (input.RawSnippet is null)
            throw new ArgumentNullException(nameof(input), "RawSnippet får inte vara null. Använd string.Empty om inget snippet finns.");
    }

    /// <summary>
    /// Konverterar byte-array till hex-sträng med lowercase — utan Linq-overhead.
    /// T.ex. [0x4A, 0x2F] → "4a2f"
    /// </summary>
    private static string ToHexLower(byte[] bytes)
    {
        // Varje byte → 2 hex-tecken, totalt 64 tecken för SHA-256
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            var b  = bytes[i];
            chars[i * 2]     = GetHexChar(b >> 4);
            chars[i * 2 + 1] = GetHexChar(b & 0xF);
        }
        return new string(chars);
    }

    private static char GetHexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);
}
