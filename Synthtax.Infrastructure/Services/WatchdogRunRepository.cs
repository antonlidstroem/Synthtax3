using Microsoft.EntityFrameworkCore;
using Synthtax.API.Controllers;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

/// <summary>
/// EF-implementation av <see cref="IWatchdogRunRepository"/>.
/// Sparar och hämtar körningsprotokoll för watchdog-bakgrundstjänsten.
/// </summary>
public sealed class WatchdogRunRepository : IWatchdogRunRepository
{
    private readonly SynthtaxDbContext _db;

    public WatchdogRunRepository(SynthtaxDbContext db) => _db = db;

    public async Task<WatchdogRun?> GetLastRunAsync(
        WatchdogSource source, CancellationToken ct = default) =>
        await _db.WatchdogRuns
            .IgnoreQueryFilters()
            .Where(r => r.Source == source)
            .OrderByDescending(r => r.RanAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<WatchdogRun>> GetRecentRunsAsync(
        int limit, CancellationToken ct = default) =>
        await _db.WatchdogRuns
            .IgnoreQueryFilters()
            .OrderByDescending(r => r.RanAt)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task SaveRunAsync(WatchdogRun run, CancellationToken ct = default)
    {
        _db.WatchdogRuns.Add(run);
        await _db.SaveChangesAsync(ct);
    }
}
