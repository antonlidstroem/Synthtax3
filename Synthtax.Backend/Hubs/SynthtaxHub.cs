using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Synthtax.Backend.Hubs;

/// <summary>
/// FÖRBÄTTRING #4+5:
///   - [Authorize] lagd till — hubben var öppen för alla utan autentisering.
///   - JoinOrgGroup validerar nu att inkommande token faktiskt tillhör den org
///     som begärs, annars kan valfri klient prenumerera på vilken organisations
///     events som helst.
/// </summary>
[Authorize]
public sealed class SynthtaxHub : Hub
{
    private readonly ILogger<SynthtaxHub> _logger;

    // Kräver att JWT innehåller detta claim (sätts av Synthtax.API.SaaS.JWT)
    private const string OrgIdClaim = "synthtax:org_id";

    public SynthtaxHub(ILogger<SynthtaxHub> logger)
        => _logger = logger;

    public async Task JoinOrgGroup(string organizationId)
    {
        // ── Säkerhetsvalidering ───────────────────────────────────────────────
        var claimedOrgId = Context.User?.FindFirstValue(OrgIdClaim);

        if (claimedOrgId is null || claimedOrgId != organizationId)
        {
            _logger.LogWarning(
                "Anslutning {ConnId} försökte gå med i org:{OrgId} men token säger org:{Claimed}.",
                Context.ConnectionId, organizationId, claimedOrgId ?? "(saknas)");

            // Kasta SecurityException — SignalR skickar ett fel till klienten
            throw new HubException("Otillåtet: du kan bara prenumerera på din egen organisations events.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{organizationId}");
        _logger.LogDebug("Anslutning {ConnId} gick med i org:{OrgId}.",
            Context.ConnectionId, organizationId);
    }

    public async Task LeaveOrgGroup(string organizationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"org:{organizationId}");
        _logger.LogDebug("Anslutning {ConnId} lämnade org:{OrgId}.",
            Context.ConnectionId, organizationId);
    }

    public Task AcknowledgeHeartbeat() => Task.CompletedTask;

    public override Task OnConnectedAsync()
    {
        var orgId = Context.User?.FindFirstValue(OrgIdClaim) ?? "(okänd)";
        _logger.LogDebug("Klient ansluten: {ConnId}, org={OrgId}.",
            Context.ConnectionId, orgId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
            _logger.LogWarning(exception,
                "Klient frånkopplad med fel: {ConnId}.", Context.ConnectionId);
        else
            _logger.LogDebug("Klient frånkopplad: {ConnId}.", Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }
}
