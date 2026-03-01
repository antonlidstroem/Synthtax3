using System.Text.Json;
using Synthtax.Core.Contracts;
using Synthtax.Core.Fingerprinting;
using Synthtax.Core.Orchestration;
using Synthtax.Domain.Entities;

namespace Synthtax.Application.Orchestration;

/// <summary>
/// Ren diff-motor — beräknar exakt vad som ska synkroniseras med databasen
/// utan att göra ett enda DB-anrop.
///
/// <para><b>Trestegsdiff:</b>
/// <list type="number">
///   <item><b>Match</b> — fingerprint finns i både scan och DB → uppdatera metadata.</item>
///   <item><b>Re-open</b> — fingerprint matchar ett auto-stängt item → återöppna.</item>
///   <item><b>New</b> — fingerprint finns bara i scan → skapa nytt item.</item>
///   <item><b>Auto-close</b> — fingerprint finns bara i DB (aktivt) → stäng automatiskt.</item>
/// </list>
/// </para>
///
/// <para><b>Statusinvarianter</b> som respekteras:
/// <list type="bullet">
///   <item><c>Accepted</c> och <c>FalsePositive</c> berörs ALDRIG — mänskliga beslut.</item>
///   <item>Bara auto-stängda (<c>AutoClosed == true</c>) Resolved-items kan återöppnas.</item>
///   <item>Manuellt stängda Resolved-items berörs inte.</item>
/// </list>
/// </para>
/// </summary>
public sealed class SyncEngine
{
    private readonly IFingerprintService _fingerprinter;

    public SyncEngine(IFingerprintService fingerprinter)
    {
        _fingerprinter = fingerprinter;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Publik API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Beräknar differensen mellan inkommande scan-resultat och befintliga DB-poster.
    /// </summary>
    /// <param name="projectId">Projektets ID — del av fingerprint-nyckel.</param>
    /// <param name="scannedIssues">Alla issues som hittades i denna skanningsomgång.</param>
    /// <param name="existingItems">
    /// Alla aktiva (icke soft-deletade) BacklogItems för projektet, indexerade på Fingerprint.
    /// Hämtas av anroparen för att hålla SyncEngine ren.
    /// </param>
    public SyncDiff Compute(
        Guid                                projectId,
        IReadOnlyList<RawIssue>             scannedIssues,
        IReadOnlyDictionary<string, BacklogItem> existingItems)
    {
        // ── Steg 1: Bygg fingerprint-karta för scan ──────────────────────
        var scanMap = BuildScanMap(projectId, scannedIssues);

        // ── Steg 2: Klassificera varje scan-issue ─────────────────────────
        var toCreate    = new List<NewItemSpec>();
        var toUpdate    = new List<MatchedItemSpec>();
        var toReopen    = new List<ReopenItemSpec>();

        foreach (var (fingerprint, issue) in scanMap)
        {
            var metaJson = SerializeMetadata(issue);

            if (!existingItems.TryGetValue(fingerprint, out var existing))
            {
                // ── NYTT: fingerprint saknas i DB ─────────────────────────
                toCreate.Add(new NewItemSpec(
                    Fingerprint:        fingerprint,
                    RuleId:             issue.RuleId,
                    MetadataJson:       metaJson,
                    EffectiveSeverity:  issue.Severity));
            }
            else if (BacklogItemStatus.ShouldReopen(existing.Status, existing.AutoClosed))
            {
                // ── ÅTERÖPPNA: auto-stängt ärende har dykt upp igen ───────
                toReopen.Add(new ReopenItemSpec(existing.Id, metaJson));
            }
            else if (!BacklogItemStatus.IsTerminal(existing.Status))
            {
                // ── MATCHA: aktivt ärende — uppdatera bara metadata ───────
                toUpdate.Add(new MatchedItemSpec(existing.Id, metaJson));
            }
            // Terminal (Accepted, FalsePositive) → ignoreras tyst
        }

        // ── Steg 3: Hitta aktiva items som SAKNAS i skanningen ────────────
        var scannedFingerprints = scanMap.Keys.ToHashSet(StringComparer.Ordinal);

        var toAutoClose = existingItems.Values
            .Where(bi => BacklogItemStatus.IsActive(bi.Status)
                      && !scannedFingerprints.Contains(bi.Fingerprint))
            .Select(bi => new AutoCloseItemSpec(bi.Id))
            .ToList();

        return new SyncDiff
        {
            ToCreate    = toCreate.AsReadOnly(),
            ToUpdate    = toUpdate.AsReadOnly(),
            ToReopen    = toReopen.AsReadOnly(),
            ToAutoClose = toAutoClose.AsReadOnly()
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privata hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Beräknar fingerprints för alla scan-issues och bygger en fingerprint→issue-karta.
    /// Vid kollision (exakt samma fingerprint för två issues) vinner den sista —
    /// det är ett plugin-fel, men kraschar inte systemet.
    /// </summary>
    private Dictionary<string, RawIssue> BuildScanMap(
        Guid projectId, IReadOnlyList<RawIssue> issues)
    {
        var inputs = issues
            .Select(i => FingerprintInput.FromRawIssue(i, projectId))
            .ToList()
            .AsReadOnly();

        var hashes = _fingerprinter.ComputeBatch(inputs);

        var map = new Dictionary<string, RawIssue>(
            capacity:  issues.Count,
            comparer:  StringComparer.Ordinal);

        for (int i = 0; i < issues.Count; i++)
            map[hashes[i]] = issues[i];   // sista vinner vid kollision

        return map;
    }

    /// <summary>
    /// Serialiserar all relevant plats- och kontextinformation från en RawIssue
    /// till den JSON-blob som lagras i BacklogItem.Metadata.
    /// </summary>
    private static string SerializeMetadata(RawIssue issue)
    {
        var meta = new
        {
            filePath      = issue.FilePath,
            startLine     = issue.StartLine,
            endLine       = issue.EndLine,
            startColumn   = issue.StartColumn,
            snippet       = TruncateSnippet(issue.Snippet, 500),
            message       = issue.Message,
            suggestion    = issue.Suggestion,
            scope         = issue.Scope.ToString(),
            category      = issue.Category,
            isAutoFixable = issue.IsAutoFixable,
            fixedSnippet  = issue.IsAutoFixable ? TruncateSnippet(issue.FixedSnippet, 500) : null,
            pluginMeta    = issue.Metadata
        };

        return JsonSerializer.Serialize(meta, MetaSerializerOptions);
    }

    private static string? TruncateSnippet(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max] + "…";

    private static readonly JsonSerializerOptions MetaSerializerOptions = new()
    {
        WriteIndented          = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
