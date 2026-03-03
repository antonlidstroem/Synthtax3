using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Synthtax.API.Hubs;

/// <summary>Hub för SuperAdmin-realtidsnotifikationer.</summary>
public sealed class AdminHub : Hub
{
    private readonly ILogger<AdminHub> _logger;

    public AdminHub(ILogger<AdminHub> logger)
        => _logger = logger;

    public override Task OnConnectedAsync()
    {
        _logger.LogDebug("Admin ansluten: {ConnId}.", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Admin frånkopplad: {ConnId}.", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
