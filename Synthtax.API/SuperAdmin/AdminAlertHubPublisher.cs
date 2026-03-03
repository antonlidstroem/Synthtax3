using Microsoft.AspNetCore.SignalR;
using Synthtax.API.Hubs;

namespace Synthtax.API.SuperAdmin;

/// <summary>Definierar kontrakt för push-notiser till super-admin panelen.</summary>
public interface IAdminAlertPublisher
{
    Task PublishAsync(AdminAlertNotification alert, CancellationToken ct = default);
}

/// <summary>Payload för AdminAlert SignalR-event.</summary>
public sealed record AdminAlertNotification(
    Guid     AlertId,
    string   Title,
    string   Severity,
    string   Source,
    DateTime CreatedAt);

/// <summary>
/// Publicerar watchdog-larm till alla inloggade super-admins via SignalR AdminHub.
/// </summary>
public sealed class AdminAlertHubPublisher : IAdminAlertPublisher
{
    public const string AdminGroupName    = "SuperAdmins";
    public const string AlertUpdatedMethod = "WatchdogAlertUpdated";
    public const string NewAlertMethod    = "WatchdogAlertNew";

    private readonly IHubContext<AdminHub> _hub;

    public AdminAlertHubPublisher(IHubContext<AdminHub> hub) => _hub = hub;

    public Task PublishAsync(AdminAlertNotification alert, CancellationToken ct = default)
        => _hub.Clients.Group(AdminGroupName)
               .SendAsync(NewAlertMethod, alert, ct);
}
