using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Synthtax.Core.Orchestration;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;

namespace Synthtax.Application.Orchestration;

/// <summary>
/// Skriver en <see cref="SyncDiff"/> till databasen med rätt strategi baserat på volym.
///
/// <para><b>Standardstrategi (≤ bulkThreshold aktiva items):</b><br/>
/// Laddar berörda entiteter i EF Core change tracker, modifierar dem och
/// kör SaveChanges. Ger full audit-trail via <c>AuditSaveChangesInterceptor</c>.</para>
///
/// <para><b>Bulk-strategi (> bulkThreshold aktiva items):</b><br/>
/// Använder EF Core 8:s <c>ExecuteUpdateAsync</c> som genererar optimerade
/// SQL SET-satser utan att ladda entiteter. Audit-fält sätts explicit i lambdan.
/// INSERTs körs i batchar om 500 via <c>AddRange</c> + <c>SaveChanges</c>.</para>
///
/// <para><b>Parameterbegränsning:</b><br/>
/// SQL Server tillåter max 2 100 parametrar per query. Items med >500 IDs chunkas
/// automatiskt i båda strategierna.</para>
/// </summary>
public sealed class SyncWriter
{
    private readonly SynthtaxDbContext          _db;
    private readonly ILogger<SyncWriter>        _logger;

    private const int ChunkSize = 500;   // Säker gräns under SQL Servers 2 100-parameter-limit

    public SyncWriter(SynthtaxDbContext db, ILogger<SyncWriter> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Publik API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Väljer och kör rätt skrivarstrategi.
    /// Anropas av orchestratorn inuti en öppen <c>IDbContextTransaction</c>.
    /// </summary>
    public async Task WriteAsync(
        SyncDiff              diff,
        Guid                  projectId,
        Guid                  tenantId,
        Guid                  sessionId,
        int                   activeItemCount,
        int                   bulkThreshold,
        CancellationToken     ct)
    {
        if (diff.IsEmpty)
        {
            _logger.LogDebug("SyncDiff är tom — inga DB-skrivningar nödvändiga.");
            return;
        }

        if (activeItemCount > bulkThreshold)
        {
            _logger.LogInformation(
                "Bulk-strategi vald: {Active} aktiva items > tröskel {Threshold}.",
                activeItemCount, bulkThreshold);
            await WriteBulkAsync(diff, projectId, tenantId, sessionId, ct);
        }
        else
        {
            _logger.LogDebug(
                "Standard-strategi vald: {Active} aktiva items ≤ tröskel {Threshold}.",
                activeItemCount, bulkThreshold);
            await WriteStandardAsync(diff, projectId, tenantId, sessionId, ct);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // STANDARD-strategi — change tracker (≤ bulkThreshold)
    // ═══════════════════════════════════════════════════════════════════════

    private async Task WriteStandardAsync(
        SyncDiff diff, Guid projectId, Guid tenantId, Guid sessionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // ── Skapa nya items ────────────────────────────────────────────────
        var newItems = diff.ToCreate.Select(spec => new BacklogItem
        {
            Id                   = Guid.NewGuid(),
            ProjectId            = projectId,
            TenantId             = tenantId,
            RuleId               = spec.RuleId,
            Fingerprint          = spec.Fingerprint,
            Status               = BacklogStatus.Open,
            Metadata             = spec.MetadataJson,
            LastSeenInSessionId  = sessionId,
            AutoClosed           = false
        }).ToList();
        _db.BacklogItems.AddRange(newItems);

        // ── Hämta och uppdatera matchade items ────────────────────────────
        if (diff.ToUpdate.Count > 0)
        {
            var updateIds = diff.ToUpdate.Select(s => s.BacklogItemId).ToList();
            var updateMap = diff.ToUpdate.ToDictionary(s => s.BacklogItemId);
            var items     = await LoadByIdsChunkedAsync(updateIds, ct);

            foreach (var item in items)
            {
                if (!updateMap.TryGetValue(item.Id, out var spec)) continue;
                item.Metadata            = spec.NewMetadataJson;
                item.LastSeenInSessionId = sessionId;
            }
        }

        // ── Hämta och återöppna auto-stängda items ────────────────────────
        if (diff.ToReopen.Count > 0)
        {
            var reopenIds = diff.ToReopen.Select(s => s.BacklogItemId).ToList();
            var reopenMap = diff.ToReopen.ToDictionary(s => s.BacklogItemId);
            var items     = await LoadByIdsChunkedAsync(reopenIds, ct);

            foreach (var item in items)
            {
                if (!reopenMap.TryGetValue(item.Id, out var spec)) continue;
                item.Status                = BacklogStatus.Open;
                item.AutoClosed            = false;
                item.AutoClosedInSessionId = null;
                item.ReopenedInSessionId   = sessionId;
                item.Metadata              = spec.NewMetadataJson;
                item.LastSeenInSessionId   = sessionId;
            }
        }

        // ── Hämta och auto-stäng försvunna items ──────────────────────────
        if (diff.ToAutoClose.Count > 0)
        {
            var closeIds = diff.ToAutoClose.Select(s => s.BacklogItemId).ToList();
            var items    = await LoadByIdsChunkedAsync(closeIds, ct);

            foreach (var item in items)
            {
                item.Status                = BacklogStatus.Resolved;
                item.AutoClosed            = true;
                item.AutoClosedInSessionId = sessionId;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Standard-sync klar: +{New} skapade, {Updated} uppdaterade, " +
            "{Reopened} återöppnade, {Closed} auto-stängda.",
            diff.NewCount, diff.ToUpdate.Count, diff.ToReopen.Count, diff.AutoCloseCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BULK-strategi — ExecuteUpdateAsync (> bulkThreshold)
    // ═══════════════════════════════════════════════════════════════════════

    private async Task WriteBulkAsync(
        SyncDiff diff, Guid projectId, Guid tenantId, Guid sessionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // ── INSERT: nya items i batchar ────────────────────────────────────
        if (diff.ToCreate.Count > 0)
        {
            var chunks = diff.ToCreate.Chunk(ChunkSize);
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                var items = chunk.Select(spec => new BacklogItem
                {
                    Id                   = Guid.NewGuid(),
                    ProjectId            = projectId,
                    TenantId             = tenantId,
                    RuleId               = spec.RuleId,
                    Fingerprint          = spec.Fingerprint,
                    Status               = BacklogStatus.Open,
                    Metadata             = spec.MetadataJson,
                    LastSeenInSessionId  = sessionId,
                    AutoClosed           = false,
                    // Audit-fält sätts explicit (interceptorn gör det normalt)
                    CreatedAt            = now,
                    LastModifiedAt       = now
                });
                _db.BacklogItems.AddRange(items);
                await _db.SaveChangesAsync(ct);
            }
        }

        // ── UPDATE matchade: metadata + lastSeen ──────────────────────────
        if (diff.ToUpdate.Count > 0)
        {
            // OBS: ExecuteUpdateAsync stöder inte per-entitet metadata-uppdatering.
            // Vi uppdaterar lastSeen i bulk och metadata via change-tracker i chunk.
            // Kompromiss: metadata uppdateras i chunk om 500 via change tracker.
            var updateIdChunks = diff.ToUpdate.Select(s => s.BacklogItemId).Chunk(ChunkSize);
            var metaMap        = diff.ToUpdate.ToDictionary(s => s.BacklogItemId);

            foreach (var chunk in updateIdChunks)
            {
                ct.ThrowIfCancellationRequested();
                var chunkSet = chunk.ToHashSet();
                var items    = await _db.BacklogItems
                    .Where(bi => chunkSet.Contains(bi.Id))
                    .ToListAsync(ct);

                foreach (var item in items)
                {
                    item.LastSeenInSessionId = sessionId;
                    if (metaMap.TryGetValue(item.Id, out var spec))
                        item.Metadata = spec.NewMetadataJson;
                    item.LastModifiedAt = now;
                }
                await _db.SaveChangesAsync(ct);
            }
        }

        // ── BULK UPDATE: återöppna auto-stängda items ─────────────────────
        if (diff.ToReopen.Count > 0)
        {
            var reopenIdChunks = diff.ToReopen.Select(s => s.BacklogItemId).Chunk(ChunkSize);
            foreach (var chunk in reopenIdChunks)
            {
                ct.ThrowIfCancellationRequested();
                var chunkSet = chunk.ToHashSet();

                // ExecuteUpdateAsync: en SQL UPDATE-sats, inga entiteter i minnet
                await _db.BacklogItems
                    .Where(bi => chunkSet.Contains(bi.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(bi => bi.Status,                BacklogStatus.Open)
                        .SetProperty(bi => bi.AutoClosed,            false)
                        .SetProperty(bi => bi.AutoClosedInSessionId, (Guid?)null)
                        .SetProperty(bi => bi.ReopenedInSessionId,   sessionId)
                        .SetProperty(bi => bi.LastSeenInSessionId,   sessionId)
                        .SetProperty(bi => bi.LastModifiedAt,        now),
                    ct);
            }

            // Metadata per item — kräver change tracker (kan inte göras per-rad i ExecuteUpdate)
            var reopenMetaMap = diff.ToReopen.ToDictionary(s => s.BacklogItemId);
            var reopenAllIds  = diff.ToReopen.Select(s => s.BacklogItemId).ToHashSet();
            foreach (var chunk in reopenAllIds.Chunk(ChunkSize))
            {
                ct.ThrowIfCancellationRequested();
                var chunkSet = chunk.ToHashSet();
                var items    = await _db.BacklogItems
                    .Where(bi => chunkSet.Contains(bi.Id))
                    .ToListAsync(ct);
                foreach (var item in items)
                {
                    if (reopenMetaMap.TryGetValue(item.Id, out var spec))
                        item.Metadata = spec.NewMetadataJson;
                }
                await _db.SaveChangesAsync(ct);
            }
        }

        // ── BULK UPDATE: auto-stäng försvunna items ───────────────────────
        if (diff.ToAutoClose.Count > 0)
        {
            var closeIdChunks = diff.ToAutoClose.Select(s => s.BacklogItemId).Chunk(ChunkSize);
            foreach (var chunk in closeIdChunks)
            {
                ct.ThrowIfCancellationRequested();
                var chunkSet = chunk.ToHashSet();

                await _db.BacklogItems
                    .Where(bi => chunkSet.Contains(bi.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(bi => bi.Status,                BacklogStatus.Resolved)
                        .SetProperty(bi => bi.AutoClosed,            true)
                        .SetProperty(bi => bi.AutoClosedInSessionId, sessionId)
                        .SetProperty(bi => bi.LastModifiedAt,        now),
                    ct);
            }
        }

        _logger.LogInformation(
            "Bulk-sync klar: +{New} skapade, {Updated} uppdaterade, " +
            "{Reopened} återöppnade, {Closed} auto-stängda.",
            diff.NewCount, diff.ToUpdate.Count, diff.ToReopen.Count, diff.AutoCloseCount);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Laddar BacklogItems via en lista av IDs, chunkat för att undvika
    /// SQL Servers 2 100-parameter-begränsning.
    /// </summary>
    private async Task<List<BacklogItem>> LoadByIdsChunkedAsync(
        List<Guid> ids, CancellationToken ct)
    {
        var result = new List<BacklogItem>(ids.Count);
        foreach (var chunk in ids.Chunk(ChunkSize))
        {
            ct.ThrowIfCancellationRequested();
            var chunkSet = chunk.ToHashSet();
            var items    = await _db.BacklogItems
                .Where(bi => chunkSet.Contains(bi.Id))
                .ToListAsync(ct);
            result.AddRange(items);
        }
        return result;
    }
}
