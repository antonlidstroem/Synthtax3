using Synthtax.Core.Entities;

namespace Synthtax.Application.Watchdog;

/// <summary>
/// Repository for persisting and querying watchdog run results.
/// Defined in Application, implemented in Infrastructure.
/// </summary>
public interface IWatchdogRunRepository
{
    Task<WatchdogRun?>               GetLastRunAsync(WatchdogSource source, CancellationToken ct = default);
    Task<IReadOnlyList<WatchdogRun>> GetRecentRunsAsync(int limit, CancellationToken ct = default);
    Task                             SaveRunAsync(WatchdogRun run, CancellationToken ct = default);
}
