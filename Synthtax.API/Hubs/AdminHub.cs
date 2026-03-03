using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Synthtax.API.Hubs;

/// <summary>
/// SignalR-hub för super-admin panelen.
/// Alla inloggade super-admins läggs automatiskt till i gruppen "SuperAdmins"
/// vid anslutning, och tar emot push-notiser om watchdog-larm.
/// </summary>
[Authorize]
public sealed class AdminHub : Hub
{
    public const string GroupName = "SuperAdmins";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName);
        await base.OnDisconnectedAsync(exception);
    }
}
