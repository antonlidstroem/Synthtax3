using Synthtax.Core.Entities;

namespace Synthtax.Application.Watchdog;

/// <summary>
/// Publishes watchdog alert notifications to connected admin clients.
/// Implemented in the API layer (SignalR) but consumed in Infrastructure and Application.
/// </summary>
public interface IAdminAlertPublisher
{
    Task PublishNewAlertAsync(WatchdogAlert alert, CancellationToken ct = default);
}
