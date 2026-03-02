using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Synthtax.Shared.SignalR;

namespace Synthtax.Backend.Hubs;

/// <summary>
/// Synthtax SignalR Hub — server-sidan av realtidssynkroniseringen.
///
/// <para><b>Grupper:</b>
/// Varje organisation har en dedikerad SignalR-grupp: <c>org:{orgId}</c>.
/// Klienter prenumererar via <c>JoinOrganization</c> och events broadcastas
/// enbart till den berörda organisationens grupp.</para>
///
/// <para><b>Autentisering:</b>
/// JWT-token från Fas 5 valideras av <c>[Authorize]</c>.
/// OrganizationId läses från claim <c>synthtax:org_id</c>.</para>
///
/// <para><b>Skalning:</b>
/// Registreras med <c>AddStackExchangeRedisBackplane</c> i produktion
/// (se <see cref="SignalRServiceExtensions"/>).</para>
/// </summary>
[Authorize]
public sealed class SynthtaxHub : Hub
{
    private readonly ILogger<SynthtaxHub> _logger;

    // Grupp-namnkonvention: "org:{guid:N}"
    private static string OrgGroup(Guid orgId) => $"org:{orgId:N}";

    public SynthtaxHub(ILogger<SynthtaxHub> logger)
    {
        _logger = logger;
    }

    // ── Anslutning ────────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var orgId = GetOrganizationId();
        if (orgId is null)
        {
            _logger.LogWarning(
                "SignalR: Klient {ConnId} saknar org_id-claim — kopplar ner.",
                Context.ConnectionId);
            Context.Abort();
            return;
        }

        // Lägg till klienten i organisations-gruppen
        await Groups.AddToGroupAsync(Context.ConnectionId, OrgGroup(orgId.Value));

        _logger.LogInformation(
            "SignalR: Klient {ConnId} ansluten till org {OrgId}",
            Context.ConnectionId, orgId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var orgId = GetOrganizationId();
        if (orgId is not null)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId, OrgGroup(orgId.Value));

            _logger.LogInformation(
                "SignalR: Klient {ConnId} frånkopplad från org {OrgId}. Orsak: {Reason}",
                Context.ConnectionId, orgId,
                exception?.Message ?? "normal disconnection");
        }

        await base.OnDisconnectedAsync(exception);
    }

    // ── Client → Server-metoder ───────────────────────────────────────────

    /// <summary>
    /// Klienten bekräftar prenumeration (redundant med OnConnectedAsync,
    /// men kan användas efter reconnect utan full re-connect).
    /// </summary>
    public async Task JoinOrganization(Guid organizationId)
    {
        var claimOrgId = GetOrganizationId();

        // Säkerhetscheck: klienten får bara prenumerera på sin egna org
        if (claimOrgId != organizationId)
        {
            _logger.LogWarning(
                "SignalR: Klient {ConnId} försökte join org {ReqOrg} men har claim {ClaimOrg}",
                Context.ConnectionId, organizationId, claimOrgId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, OrgGroup(organizationId));
    }

    public async Task LeaveOrganization(Guid organizationId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId, OrgGroup(organizationId));
    }

    /// <summary>Klienten pongat ett heartbeat — loggad för diagnostik.</summary>
    public Task AcknowledgeHeartbeat(DateTime clientReceivedAt)
    {
        var latency = DateTime.UtcNow - clientReceivedAt;
        _logger.LogDebug(
            "SignalR heartbeat ACK från {ConnId} — latens {Ms} ms",
            Context.ConnectionId, latency.TotalMilliseconds);
        return Task.CompletedTask;
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private Guid? GetOrganizationId()
    {
        var raw = Context.User?.FindFirst("synthtax:org_id")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ISynthtaxHubPusher  —  intern tjänst för att pusha events från bakgrundsjobbbet
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kontrakt för att skicka events till anslutna VSIX-klienter.
///
/// <para>Implementeras av <see cref="SynthtaxHubPusher"/> och injiceras i
/// analysationsorkestratorn (Fas 3) och bakgrundsjobbbet som triggar analyser.</para>
///
/// <para><b>Användning i Fas 3-orchestratorn:</b>
/// <code>
///   // Efter SyncAsync() har kommit tillbaka
///   await _hubPusher.PushAnalysisUpdatedAsync(new AnalysisUpdatedPayload { ... });
/// </code>
/// </para>
/// </summary>
public interface ISynthtaxHubPusher
{
    /// <summary>Pushar ett AnalysisUpdated-event till alla klienter i organisationens grupp.</summary>
    Task PushAnalysisUpdatedAsync(AnalysisUpdatedPayload payload, CancellationToken ct = default);

    /// <summary>Pushar ett IssueStatusChanged-event.</summary>
    Task PushIssueStatusChangedAsync(IssueStatusChangedPayload payload, CancellationToken ct = default);

    /// <summary>Pushar ett LicenseChanged-event.</summary>
    Task PushLicenseChangedAsync(LicenseChangedPayload payload, CancellationToken ct = default);

    /// <summary>Skickar heartbeat till alla anslutna klienter.</summary>
    Task PushHeartbeatAsync(HeartbeatPayload payload, CancellationToken ct = default);
}

/// <summary>
/// Implementering av <see cref="ISynthtaxHubPusher"/> via <c>IHubContext{SynthtaxHub}</c>.
/// Registreras som Singleton — IHubContext är thread-safe.
/// </summary>
public sealed class SynthtaxHubPusher : ISynthtaxHubPusher
{
    private readonly IHubContext<SynthtaxHub> _hub;
    private readonly ILogger<SynthtaxHubPusher> _logger;

    private static string OrgGroup(Guid orgId) => $"org:{orgId:N}";

    public SynthtaxHubPusher(
        IHubContext<SynthtaxHub>    hub,
        ILogger<SynthtaxHubPusher> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public async Task PushAnalysisUpdatedAsync(AnalysisUpdatedPayload payload, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SignalR push AnalysisUpdated → org {OrgId} (+{New} -{Resolved})",
            payload.OrganizationId, payload.NewIssuesCount, payload.ResolvedIssuesCount);

        await _hub.Clients
            .Group(OrgGroup(payload.OrganizationId))
            .SendAsync(HubMethods.AnalysisUpdated, payload, ct);
    }

    public async Task PushIssueStatusChangedAsync(IssueStatusChangedPayload payload, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "SignalR push IssueStatusChanged → org {OrgId} issue {IssueId} {Old}→{New}",
            payload.OrganizationId, payload.IssueId, payload.OldStatus, payload.NewStatus);

        await _hub.Clients
            .Group(OrgGroup(payload.OrganizationId))
            .SendAsync(HubMethods.IssueStatusChanged, payload, ct);
    }

    public async Task PushLicenseChangedAsync(LicenseChangedPayload payload, CancellationToken ct = default)
    {
        await _hub.Clients
            .Group(OrgGroup(payload.OrganizationId))
            .SendAsync(HubMethods.LicenseChanged, payload, ct);
    }

    public async Task PushHeartbeatAsync(HeartbeatPayload payload, CancellationToken ct = default)
    {
        // Skickas till ALLA anslutna klienter (inte grupp-specifikt)
        await _hub.Clients.All.SendAsync(HubMethods.Heartbeat, payload, ct);
    }
}
