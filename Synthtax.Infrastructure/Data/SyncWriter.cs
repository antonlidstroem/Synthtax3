using Microsoft.EntityFrameworkCore;
using Synthtax.Core.Orchestration;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;

namespace Synthtax.Infrastructure.Data;

public class SyncWriter
{
    private readonly SynthtaxDbContext _db;

    public SyncWriter(SynthtaxDbContext db) => _db = db;

    public async Task<SyncWriteResult> WriteAsync(SyncDiff diff, OrchestratorRequest request, Guid sessionId, CancellationToken ct)
    {
        var addedItems = new List<BacklogItem>();

        // 1. Skapa nya issues
        foreach (var spec in diff.ToCreate)
        {
            var newItem = new BacklogItem
            {
                ProjectId = request.ProjectId,
                TenantId = request.TenantId,
                RuleId = spec.RuleId,
                Fingerprint = spec.Fingerprint,
                Metadata = spec.MetadataJson,
                Status = BacklogStatus.Open,
                CreatedAt = DateTime.UtcNow
            };
            _db.BacklogItems.Add(newItem);
            addedItems.Add(newItem);
        }

        // 2. Auto-stäng försvunna issues (Fas 3)
        foreach (var spec in diff.ToAutoClose)
        {
            var item = await _db.BacklogItems.FindAsync(spec.BacklogItemId);
            if (item != null)
            {
                item.Status = BacklogStatus.Resolved;
                item.AutoClosed = true;
                item.AutoClosedInSessionId = sessionId;
            }
        }

        // 3. Återöppna issues (Fas 3)
        foreach (var spec in diff.ToReopen)
        {
            var item = await _db.BacklogItems.FindAsync(spec.BacklogItemId);
            if (item != null)
            {
                item.Status = BacklogStatus.Open;
                item.AutoClosed = false;
                item.ReopenedInSessionId = sessionId;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new SyncWriteResult { AddedItems = addedItems };
    }
}

public class SyncWriteResult
{
    public List<BacklogItem> AddedItems { get; set; } = new();
}