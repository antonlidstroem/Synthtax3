using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.Orchestration;
using Synthtax.Core.FuzzyMatching;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Application.Orchestration;

/// <summary>
/// Utökar <see cref="SyncWriter"/> med hantering av
/// <see cref="SyncDiffV4.ToFuzzyUpdate"/>-items.
///
/// <para><b>Vad som skrivs för ett fuzzy-matchat item:</b>
/// <list type="bullet">
///   <item><c>Fingerprint</c> → nytt fingerprint (koden har driftat).</item>
///   <item><c>PreviousFingerprints</c> → JSON-array med historik (max 10 poster).</item>
///   <item><c>Metadata</c> → uppdaterad position och snippet.</item>
///   <item><c>LastSeenInSessionId</c> → aktuell session.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FuzzyAwareSyncWriter
{
    private readonly SyncWriter              _baseWriter;
    private readonly SynthtaxDbContext       _db;
    private readonly ILogger<FuzzyAwareSyncWriter> _logger;

    private const int ChunkSize              = 500;
    private const int MaxFingerprintHistory  = 10;

    public FuzzyAwareSyncWriter(
        SyncWriter                    baseWriter,
        SynthtaxDbContext             db,
        ILogger<FuzzyAwareSyncWriter> logger)
    {
        _baseWriter = baseWriter;
        _db         = db;
        _logger     = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Publik API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Skriver hela <see cref="SyncDiffV4"/> till databasen:
    /// 1. Standardsync (Fas 3) via BaseWriter.
    /// 2. Fuzzy-fingerprint-migration för fuzzy-matchade items.
    ///
    /// Anropas inom öppen IDbContextTransaction.
    /// </summary>
    public async Task WriteAsync(
        SyncDiffV4        diffV4,
        Guid              projectId,
        Guid              tenantId,
        Guid              sessionId,
        int               activeItemCount,
        int               bulkThreshold,
        CancellationToken ct)
    {
        // ── 1. Skriv exakta matchningar (Fas 3-logik) ─────────────────────
        await _baseWriter.WriteAsync(
            diffV4.Base, projectId, tenantId, sessionId,
            activeItemCount, bulkThreshold, ct);

        // ── 2. Skriv fuzzy-fingerprint-migrationer ────────────────────────
        if (diffV4.ToFuzzyUpdate.Count == 0) return;

        _logger.LogDebug(
            "FuzzyAwareSyncWriter: migrerar {Count} fingerprints.",
            diffV4.ToFuzzyUpdate.Count);

        await WriteFuzzyUpdatesAsync(diffV4.ToFuzzyUpdate, sessionId, activeItemCount, bulkThreshold, ct);

        _logger.LogInformation(
            "FuzzyAwareSyncWriter: {Count} fingerprints migrerade.",
            diffV4.ToFuzzyUpdate.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fuzzy-skrivning
    // ═══════════════════════════════════════════════════════════════════════

    private async Task WriteFuzzyUpdatesAsync(
        IReadOnlyList<FuzzyUpdateItemSpec> specs,
        Guid              sessionId,
        int               activeItemCount,
        int               bulkThreshold,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Fingerprint är unik nyckel — vi måste uppdatera den via change tracker
        // (ExecuteUpdateAsync stöder inte unik-index-brott utan ON CONFLICT)
        foreach (var chunk in specs.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            var chunkIds = chunk.Select(s => s.BacklogItemId).ToHashSet();
            var items    = await _db.BacklogItems
                .Where(bi => chunkIds.Contains(bi.Id))
                .ToListAsync(ct);

            var specById = chunk.ToDictionary(s => s.BacklogItemId);

            foreach (var item in items)
            {
                if (!specById.TryGetValue(item.Id, out var spec)) continue;

                // Migrera fingerprint
                item.Fingerprint         = spec.NewFingerprint;
                item.LastSeenInSessionId = sessionId;
                item.Metadata            = AppendFuzzyMetadata(
                    item.Metadata, spec, now);
                item.LastModifiedAt      = now;

                // Lägg till historisk fingerprint-post
                item.PreviousFingerprints = AppendFingerprintHistory(
                    item.PreviousFingerprints, spec.OldFingerprint);

                _logger.LogDebug(
                    "[FuzzyMatch] Fingerprint migrerat — ItemId:{Id} Score:{Score:F2} " +
                    "{Old}…→{New}… Strategy:{Strategy}",
                    item.Id,
                    spec.FuzzyScore,
                    spec.OldFingerprint[..Math.Min(8, spec.OldFingerprint.Length)],
                    spec.NewFingerprint[..Math.Min(8, spec.NewFingerprint.Length)],
                    spec.MatchStrategy);
            }

            await _db.SaveChangesAsync(ct);
        }
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    /// <summary>
    /// Lägger till fuzzy-matchningsdata i befintlig metadata-JSON.
    /// </summary>
    private static string AppendFuzzyMetadata(
        string? existingMetaJson,
        FuzzyUpdateItemSpec spec,
        DateTime now)
    {
        try
        {
            var dict = string.IsNullOrEmpty(existingMetaJson)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(existingMetaJson)
                  ?? new Dictionary<string, object>();

            dict["fuzzyMatchScore"]    = spec.FuzzyScore;
            dict["fuzzyMatchStrategy"] = spec.MatchStrategy.ToString();
            dict["fuzzyMatchedAt"]     = now.ToString("O");

            return JsonSerializer.Serialize(dict, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }
        catch
        {
            return existingMetaJson ?? "{}";
        }
    }

    /// <summary>
    /// Upprätthåller en JSON-array av historiska fingerprints (max 10).
    /// Format: ["abc123…", "def456…", …]
    /// </summary>
    private static string AppendFingerprintHistory(
        string? existingHistoryJson,
        string  oldFingerprint)
    {
        List<string> history;
        try
        {
            history = string.IsNullOrEmpty(existingHistoryJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(existingHistoryJson)
                  ?? new List<string>();
        }
        catch { history = new List<string>(); }

        if (!history.Contains(oldFingerprint))
            history.Add(oldFingerprint);

        // Behåll bara de senaste MaxFingerprintHistory
        if (history.Count > MaxFingerprintHistory)
            history = history.TakeLast(MaxFingerprintHistory).ToList();

        return JsonSerializer.Serialize(history);
    }
}
