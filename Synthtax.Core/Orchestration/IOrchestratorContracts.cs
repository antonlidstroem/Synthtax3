using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;
using Synthtax.Domain.Enums;

namespace Synthtax.Core.Orchestration;

// ═══════════════════════════════════════════════════════════════════════════
// OrchestratorRequest
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Input till <see cref="IAnalysisOrchestrator.RunAsync"/>.
/// Beskriver ett fullständigt analysflöde för ett projekt.
/// </summary>
public sealed record OrchestratorRequest
{
    public required Guid   ProjectId      { get; init; }
    public required Guid   TenantId       { get; init; }

    /// <summary>
    /// Lokal sökväg till projektroten (katalog eller .sln-fil).
    /// Plugins läser filer härifrån via IFileScanner.
    /// </summary>
    public required string ProjectRootPath { get; init; }

    /// <summary>
    /// Git-commit SHA vid tidpunkten för analysen.
    /// Null om projektet inte är ett git-repo (t.ex. lokalt zip-projekt).
    /// </summary>
    public string? CommitSha { get; init; }

    /// <summary>
    /// Begränsa analysen till specifika filändelser.
    /// Null = analysera alla filändelser som stöds av registrerade plugins.
    /// </summary>
    public IReadOnlySet<string>? FileExtensionFilter { get; init; }

    /// <summary>
    /// Begränsa vilka regler som körs.
    /// Null = kör alla aktiverade regler.
    /// </summary>
    public IReadOnlySet<string>? EnabledRuleIds { get; init; }

    /// <summary>
    /// Gräns för att välja bulk-skrivarstrategi.
    /// Projekt med fler aktiva BacklogItems än detta värde använder ExecuteUpdateAsync.
    /// </summary>
    public int BulkThreshold { get; init; } = 1_000;
}

// ═══════════════════════════════════════════════════════════════════════════
// OrchestratorResult
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Output från <see cref="IAnalysisOrchestrator.RunAsync"/>.
/// </summary>
public sealed record OrchestratorResult
{
    public required Guid   SessionId      { get; init; }
    public required Guid   ProjectId      { get; init; }
    public required bool   Success        { get; init; }

    // ── KPI:er ────────────────────────────────────────────────────────────
    public int    NewIssues      { get; init; }
    public int    ResolvedIssues { get; init; }
    public int    TotalIssues    { get; init; }

    /// <summary>Antal ärenden som återöppnades (Resolved → Open).</summary>
    public int    ReopenedIssues { get; init; }

    /// <summary>Aggregerat kvalitetsindex 0–100 (beräknat av KpiCalculator).</summary>
    public double OverallScore   { get; init; }

    // ── Tidsmätning ───────────────────────────────────────────────────────
    public TimeSpan TotalDuration    { get; init; }
    public TimeSpan ScanDuration     { get; init; }
    public TimeSpan SyncDuration     { get; init; }

    // ── Fel ───────────────────────────────────────────────────────────────
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool HasErrors => Errors.Count > 0;

    public override string ToString() =>
        $"Session {SessionId:N[..8]} — New:{NewIssues} Resolved:{ResolvedIssues} " +
        $"Total:{TotalIssues} Score:{OverallScore:F1} ({(Success ? "OK" : "ERROR")})";
}

// ═══════════════════════════════════════════════════════════════════════════
// SyncDiff — den beräknade differensen (ren data, ingen DB-åtkomst)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Resultatet av SyncEngine.Compute() — en ren, immutabel beskrivning
/// av exakt vad som ska skrivas till databasen.
///
/// Separerar diff-logiken (ren, testbar) från DB-skrivandet (sidoeffekter).
/// </summary>
public sealed record SyncDiff
{
    // ── Att skapa ─────────────────────────────────────────────────────────

    /// <summary>RawIssues vars fingerprint saknas i DB — ska skapas.</summary>
    public required IReadOnlyList<NewItemSpec>       ToCreate    { get; init; }

    // ── Att uppdatera ─────────────────────────────────────────────────────

    /// <summary>Befintliga items som matchades och vars metadata ska uppdateras.</summary>
    public required IReadOnlyList<MatchedItemSpec>   ToUpdate    { get; init; }

    /// <summary>Matchade items som var auto-stängda — ska återöppnas.</summary>
    public required IReadOnlyList<ReopenItemSpec>    ToReopen    { get; init; }

    /// <summary>Aktiva items vars fingerprint saknas i skanningen — ska auto-stängas.</summary>
    public required IReadOnlyList<AutoCloseItemSpec> ToAutoClose { get; init; }

    // ── Sammanfattning ────────────────────────────────────────────────────
    public int NewCount       => ToCreate.Count;
    public int MatchedCount   => ToUpdate.Count + ToReopen.Count;
    public int AutoCloseCount => ToAutoClose.Count;

    public bool IsEmpty =>
        ToCreate.Count == 0 &&
        ToUpdate.Count == 0 &&
        ToReopen.Count == 0 &&
        ToAutoClose.Count == 0;
}

/// <summary>Spec för ett nytt BacklogItem som ska skapas.</summary>
public sealed record NewItemSpec(
    string   Fingerprint,
    string   RuleId,
    string   MetadataJson,
    Severity EffectiveSeverity);

/// <summary>Spec för uppdatering av ett matchat befintligt item.</summary>
public sealed record MatchedItemSpec(
    Guid   BacklogItemId,
    string NewMetadataJson);

/// <summary>Spec för återöppning av ett auto-stängt item.</summary>
public sealed record ReopenItemSpec(
    Guid BacklogItemId,
    string NewMetadataJson);

/// <summary>Spec för auto-stängning av ett aktivt item.</summary>
public sealed record AutoCloseItemSpec(
    Guid BacklogItemId);

// ═══════════════════════════════════════════════════════════════════════════
// IAnalysisOrchestrator
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Koordinerar ett komplett analysflöde: skanning → fingerprinting → sync → KPI.
/// Hela synken körs inom en <c>IDbContextTransaction</c>.
/// </summary>
public interface IAnalysisOrchestrator
{
    /// <summary>
    /// Kör en komplett analys av projektet och synkroniserar resultatet med backloggen.
    /// </summary>
    Task<OrchestratorResult> RunAsync(
        OrchestratorRequest request,
        CancellationToken   ct = default);
}
