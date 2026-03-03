using Synthtax.Core.Contracts;
using Synthtax.Core.Entities;


namespace Synthtax.Core.FuzzyMatching;

// ═══════════════════════════════════════════════════════════════════════════
// FuzzyMatchResult
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Resultatet av en fuzzy-matchningsoperation för ett enskilt scan-issue.
/// </summary>
public sealed record FuzzyMatchResult
{
    /// <summary>Fingerprint för det inkommande scan-issue.</summary>
    public required string ScanFingerprint { get; init; }

    /// <summary>True om en tillräckligt lik kandidat hittades (score ≥ threshold).</summary>
    public bool IsMatch => MatchedItem is not null;

    /// <summary>Det befintliga BacklogItem som matchades. Null om ingen match hittades.</summary>
    public BacklogItem? MatchedItem { get; init; }

    /// <summary>Likhetsscore [0.0, 1.0] för bästa kandidat (oavsett om den översteg tröskeln).</summary>
    public double BestScore { get; init; }

    /// <summary>Antal kandidater som utvärderades.</summary>
    public int CandidatesEvaluated { get; init; }

    /// <summary>Strategi som gav matchningen.</summary>
    public FuzzyMatchStrategy MatchStrategy { get; init; }

    /// <summary>
    /// Loggraden som ska skrivas vid matchning.
    /// Format: <c>[FuzzyMatch] (Score: 0.92) RuleId:{ruleId} ScanFP:{fp[..8]} → ItemId:{id}</c>
    /// </summary>
    public string? LogEntry { get; init; }

    /// <summary>Returnerar ett icke-matchresultat med diagnostik.</summary>
    public static FuzzyMatchResult NoMatch(string fingerprint, double bestScore, int candidates) =>
        new()
        {
            ScanFingerprint     = fingerprint,
            MatchedItem         = null,
            BestScore           = bestScore,
            CandidatesEvaluated = candidates,
            MatchStrategy       = FuzzyMatchStrategy.None
        };
}

/// <summary>Strategi som ledde till en fuzzy-matchning.</summary>
public enum FuzzyMatchStrategy
{
    None       = 0,
    SameFile   = 1,   // Kandidat i samma fil, samma RuleId
    SameScope  = 2,   // Kandidat i samma semantiska scope, samma RuleId
    SameRule   = 3    // Kandidat med samma RuleId (bredaste sökning)
}

// ═══════════════════════════════════════════════════════════════════════════
// FuzzyMatchOptions
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Konfiguration för fuzzy-matchningsprocessen.
/// </summary>
public sealed record FuzzyMatchOptions
{
    /// <summary>
    /// Minsta likhetsscore för att en match ska accepteras [0.0, 1.0].
    /// Default: 0.85 per kravspecifikationen.
    /// </summary>
    public double Threshold { get; init; } = 0.85;

    /// <summary>
    /// N-gram storlek för shingling. 2 (bigrams) ger bra balans
    /// mellan specificitet och generaliseringsförmåga.
    /// </summary>
    public int NgramSize { get; init; } = 2;

    /// <summary>
    /// Max antal kandidater att utvärdera per scan-issue.
    /// Begränsar worst-case komplexitet för stora projekt.
    /// </summary>
    public int MaxCandidatesPerIssue { get; init; } = 200;

    /// <summary>
    /// Om true: utvärdera kandidater inom samma fil FÖRE kandidater i andra filer.
    /// Ger bättre precision — en issue som flyttas till en annan fil bör sannolikt
    /// inte fuzzy-matcha mot original.
    /// </summary>
    public bool PreferSameFile { get; init; } = true;

    /// <summary>Standardinställningar (per kravspecifikationen).</summary>
    public static FuzzyMatchOptions Default => new();

    /// <summary>Striktare inställningar för kritiska projekt (Tier1).</summary>
    public static FuzzyMatchOptions Strict => new() { Threshold = 0.92, MaxCandidatesPerIssue = 100 };

    /// <summary>Avslappnade inställningar för snabb prototyping.</summary>
    public static FuzzyMatchOptions Lenient => new() { Threshold = 0.75, MaxCandidatesPerIssue = 500 };
}

// ═══════════════════════════════════════════════════════════════════════════
// IFuzzyMatchService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Huvud-interface för fuzzy-matchning av issues.
/// Registreras som Singleton — implementeringen är thread-safe.
/// </summary>
public interface IFuzzyMatchService
{
    /// <summary>
    /// Försöker fuzzy-matcha ett scan-issue mot en pool av befintliga BacklogItems.
    /// </summary>
    /// <param name="scanIssue">Det inkommande issue som söker en match.</param>
    /// <param name="scanFingerprint">Exakt fingerprint för scan-issue (beräknat av FingerprintService).</param>
    /// <param name="candidates">
    /// Pool av kandidater att söka bland. Bör vara förfiltrerade på minst RuleId
    /// för att hålla antalet hanterbart.
    /// </param>
    /// <param name="options">Matchningskonfiguration. Null = Default.</param>
    FuzzyMatchResult TryMatch(
        RawIssue                   scanIssue,
        string                     scanFingerprint,
        IReadOnlyList<BacklogItem> candidates,
        FuzzyMatchOptions?         options = null);

    /// <summary>
    /// Batch-matchning: tar en lista av unmatched issues och hittar fuzzy-matchningar
    /// i en enda genomgång av kandidatpoolen. Effektivare än N × TryMatch.
    /// </summary>
    IReadOnlyList<FuzzyMatchResult> TryMatchBatch(
        IReadOnlyList<(string Fingerprint, RawIssue Issue)> unmatchedIssues,
        IReadOnlyList<BacklogItem>                          candidates,
        FuzzyMatchOptions?                                  options = null);
}
