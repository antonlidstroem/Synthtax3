using Synthtax.Core.Enums;
using Synthtax.Core.FuzzyMatching;
using Synthtax.Core.Orchestration;

namespace Synthtax.Core.FuzzyMatching;

// ═══════════════════════════════════════════════════════════════════════════
// FuzzyUpdateItemSpec
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Spec för ett BacklogItem vars fingerprint har driftat (variabel döpts om,
/// kod reformaterats) men vars strukturella likhet överstiger tröskeln.
///
/// <para>Åtgärd: uppdatera fingerprintet till det nya värdet + uppdatera metadata.
/// Nästa exakta scan kommer att ge en direkt träff igen.</para>
/// </summary>
public sealed record FuzzyUpdateItemSpec(
    /// <summary>Befintligt BacklogItem att uppdatera.</summary>
    Guid   BacklogItemId,

    /// <summary>Det gamla fingerprintet (för loggning/audit).</summary>
    string OldFingerprint,

    /// <summary>Det nya fingerprintet som nu gäller för denna issue.</summary>
    string NewFingerprint,

    /// <summary>Uppdaterad metadata-JSON från det nya scan-resultatet.</summary>
    string NewMetadataJson,

    /// <summary>Jaccard-likhetsscoren som motiverade matchningen [threshold, 1.0].</summary>
    double FuzzyScore,

    /// <summary>Strategi som gav matchningen (SameFile/SameScope/SameRule).</summary>
    FuzzyMatchStrategy MatchStrategy);

// ═══════════════════════════════════════════════════════════════════════════
// SyncDiffV4  —  Fas 3:s SyncDiff + fuzzy-tillägg
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Utökning av <see cref="SyncDiff"/> med fuzzy-matchade uppdateringar.
/// Returneras av <see cref="FuzzyAwareSyncEngine.Compute"/> istället för den grundläggande SyncDiff.
///
/// <para>Relationen till SyncDiff:
/// <list type="bullet">
///   <item><see cref="Base"/>: resultat från exakt fingerprint-matchning (oförändrat från Fas 3).</item>
///   <item><see cref="ToFuzzyUpdate"/>: items vars fingerprint driftat men som matchades strukturellt.</item>
/// </list>
/// </para>
/// </summary>
public sealed record SyncDiffV4
{
    public required SyncDiff                        Base         { get; init; }
    public required IReadOnlyList<FuzzyUpdateItemSpec> ToFuzzyUpdate { get; init; }
    public required IReadOnlyList<FuzzyMatchResult>    FuzzyLogs    { get; init; }

    // ── Delegerade properties för bekvämlighetstillgång ───────────────────
    public IReadOnlyList<NewItemSpec>       ToCreate    => Base.ToCreate;
    public IReadOnlyList<MatchedItemSpec>   ToUpdate    => Base.ToUpdate;
    public IReadOnlyList<ReopenItemSpec>    ToReopen    => Base.ToReopen;
    public IReadOnlyList<AutoCloseItemSpec> ToAutoClose => Base.ToAutoClose;

    public int FuzzyMatchCount => ToFuzzyUpdate.Count;
    public int GhostIssuesSaved => ToFuzzyUpdate.Count;  // antalet undvikta ghost issues

    public bool IsEmpty =>
        Base.IsEmpty && ToFuzzyUpdate.Count == 0;

    public override string ToString() =>
        $"SyncDiffV4 — Exact: +{Base.NewCount} new, {Base.ToUpdate.Count} match, " +
        $"{Base.ToReopen.Count} reopen, {Base.AutoCloseCount} close | " +
        $"Fuzzy: {FuzzyMatchCount} ghost-issues förhindrade";
}
