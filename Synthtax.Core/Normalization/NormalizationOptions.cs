namespace Synthtax.Core.Normalization;

/// <summary>
/// Konfigurerar vad <see cref="SnippetNormalizer"/> tar bort innan fingerprinting.
///
/// <para>Standard (<see cref="ForFingerprinting"/>) ger maximalt stabil hashing:
/// alla kommentarer och whitespace normaliseras bort.</para>
/// </summary>
public sealed record NormalizationOptions
{
    // ── Kommentarshantering ────────────────────────────────────────────────

    /// <summary>Ta bort // och # enradiga kommentarer.</summary>
    public bool StripLineComments { get; init; } = true;

    /// <summary>Ta bort /* ... */ blockkommentarer.</summary>
    public bool StripBlockComments { get; init; } = true;

    /// <summary>Ta bort """ ... """ (Python docstrings / C# raw strings).</summary>
    public bool StripTripleQuoteStrings { get; init; } = false;

    /// <summary>
    /// Ta bort Javadoc /** ... */ och XML-kommentarer &lt;summary&gt;...&lt;/summary&gt;.
    /// Inkluderas om StripBlockComments är true.
    /// </summary>
    public bool StripDocComments { get; init; } = true;

    // ── Whitespace-hantering ───────────────────────────────────────────────

    /// <summary>Kollaps alla interna whitespace-sekvenser (tabs, spaces, newlines) till ett mellanslag.</summary>
    public bool CollapseWhitespace { get; init; } = true;

    /// <summary>Ta bort leading + trailing whitespace från hela snippetet.</summary>
    public bool TrimEdges { get; init; } = true;

    // ── Case-normalisering ─────────────────────────────────────────────────

    /// <summary>
    /// Konvertera till uppercase (ToUpperInvariant) för kultur-invariant jämförelse.
    /// Nödvändigt för fingerprint-stabilitet mellan miljöer med olika kulturinställningar.
    /// </summary>
    public bool UpperCaseInvariant { get; init; } = true;

    // ── Trunkering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Max antal tecken i normaliserat snippet. 0 = ingen trunkering.
    /// Standard 128: tillräckligt för att skilja issues, men inte så långt att
    /// triviala omformatering av kod ändrar fingerprinting.
    /// </summary>
    public int MaxLength { get; init; } = 128;

    // ── Fördefinierade profiler ────────────────────────────────────────────

    /// <summary>
    /// Profil för fingerprinting — maximal stabilitet.
    /// Allt trivia bort, uppercase, trunkerat till 128 tecken.
    /// </summary>
    public static NormalizationOptions ForFingerprinting => new();

    /// <summary>
    /// Profil för display — bara whitespace-kollaps, inga case-ändringar.
    /// Kommentarer bevaras, kod visas som skriven.
    /// </summary>
    public static NormalizationOptions ForDisplay => new()
    {
        StripLineComments        = false,
        StripBlockComments       = false,
        StripDocComments         = false,
        StripTripleQuoteStrings  = false,
        CollapseWhitespace       = true,
        TrimEdges                = true,
        UpperCaseInvariant       = false,
        MaxLength                = 0
    };

    /// <summary>
    /// Profil för sökning/jämförelse — case-insensitiv men kommentarer bevarade.
    /// </summary>
    public static NormalizationOptions ForSearch => new()
    {
        StripLineComments        = false,
        StripBlockComments       = false,
        StripDocComments         = false,
        StripTripleQuoteStrings  = false,
        CollapseWhitespace       = true,
        TrimEdges                = true,
        UpperCaseInvariant       = true,
        MaxLength                = 0
    };
}

/// <summary>
/// Kommentarssyntax för olika språkfamiljer.
/// Avgör vilket strip-mönster <see cref="SnippetNormalizer"/> använder.
/// </summary>
public enum CommentStyle
{
    /// <summary>Identifiera automatiskt baserat på filändelse.</summary>
    Auto         = 0,

    /// <summary>C, C++, C#, Java, JavaScript, TypeScript, Rust, Go.</summary>
    CStyle       = 1,

    /// <summary>Python, Ruby, Shell, YAML.</summary>
    HashStyle    = 2,

    /// <summary>SQL — enkelt ( -- ) och block ( /* */ ).</summary>
    SqlStyle     = 3,

    /// <summary>HTML/XML — &lt;!-- ... --&gt;</summary>
    HtmlStyle    = 4,

    /// <summary>Ingen kommentarsstrippning.</summary>
    None         = 99
}
