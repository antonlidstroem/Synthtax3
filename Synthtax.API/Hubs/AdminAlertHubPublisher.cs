using Microsoft.AspNetCore.SignalR;
using Synthtax.Application.Watchdog;
using Synthtax.Core.Entities;

namespace Synthtax.API.Hubs;

/// <summary>
/// SignalR implementation of IAdminAlertPublisher.
/// Pushes watchdog alerts to connected admin clients via the AdminHub.
/// </summary>
public sealed class AdminAlertHubPublisher : IAdminAlertPublisher
{
    public const string AdminGroupName     = "SuperAdmins";
    public const string NewAlertMethod     = "WatchdogAlertNew";
    public const string AlertUpdatedMethod = "WatchdogAlertUpdated";

    private readonly IHubContext<AdminHub> _hub;
    private readonly ILogger<AdminAlertHubPublisher> _logger;

    public AdminAlertHubPublisher(
        IHubContext<AdminHub> hub,
        ILogger<AdminAlertHubPublisher> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public async Task PublishNewAlertAsync(WatchdogAlert alert, CancellationToken ct = default)
    {
        var payload = new
        {
            id                  = alert.Id,
            source              = alert.Source.ToString(),
            severity            = alert.Severity.ToString(),
            title               = alert.Title,
            description         = alert.Description,
            externalVersionKey  = alert.ExternalVersionKey,
            releaseNotesUrl     = alert.ReleaseNotesUrl,
            actionRequired      = alert.ActionRequired,
            externalPublishedAt = alert.ExternalPublishedAt,
            createdAt           = alert.CreatedAt
        };

        await _hub.Clients.Group(AdminGroupName)
            .SendAsync(NewAlertMethod, payload, ct);

        _logger.LogInformation(
            "Admin alert pushed: [{Severity}] {Title}", alert.Severity, alert.Title);
    }
}
