using Microsoft.AspNetCore.SignalR;
using Synthtax.Application.SuperAdmin;

namespace Synthtax.API.SuperAdmin;

public sealed class AdminAlertHubPublisher : IAdminAlertPublisher
{
    private readonly IHubContext<Hubs.AdminHub> _hub;

    public AdminAlertHubPublisher(IHubContext<Hubs.AdminHub> hub)
        => _hub = hub;

    public Task PublishAsync(AdminAlert alert, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("AdminAlert", alert, ct);
}
