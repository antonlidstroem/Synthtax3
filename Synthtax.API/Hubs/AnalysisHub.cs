using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Synthtax.API.SaaS.JWT;          // SynthtaxClaimTypes (Fas 5)
using Synthtax.Realtime.Contracts;

namespace Synthtax.API.Hubs;

/// <summary>
/// SignalR-hub för realtidsnotifieringar till VSIX-klienter.
///
/// <para><b>Routing:</b> Registreras på <c>/hubs/analysis</c> i Program.cs.</para>
///
/// <para><b>Grupper:</b>
/// Varje organisation har en grupp med ID = <c>"org:{orgId:N}"</c>.
/// Klienten ansluter och anropar <see cref="JoinOrgGroupAsync"/> direkt
/// efter handshake. Server-kod pushar via
/// <c>Clients.Group("org:{orgId:N}").Send...</c>.</para>
///
/// <para><b>Auth:</b> JWT-token (samma som REST API) krävs.
/// <c>OrganizationId</c>-claim extraheras för att styra gruppmedlemskap —
/// en klient kan bara gå med i sin egen orgs grupp.</para>
/// </summary>
[Authorize]
public sealed class AnalysisHub : Hub
{
    private readonly ILogger<AnalysisHub> _logger;
    private readonly IAnalysisHubPublisher _publisher;

    public AnalysisHub(ILogger<AnalysisHub> logger, IAnalysisHubPublisher publisher)
    {
        _logger    = logger;
        _publisher = publisher;
    }

    // ── Livscykel ──────────────────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var orgId = GetOrgId();
        _logger.LogInformation(
            "VSIX client connected. ConnectionId={Id} OrgId={Org}",
            Context.ConnectionId, orgId);

        if (orgId is not null)
        {
            // Auto-join org-grupp vid anslutning om claim är känd
            await Groups.AddToGroupAsync(Context.ConnectionId, OrgGroupName(orgId.Value));
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var orgId = GetOrgId();
        if (orgId is not null)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, OrgGroupName(orgId.Value));

        _logger.LogInformation(
            "VSIX client disconnected. ConnectionId={Id} Reason={Reason}",
            Context.ConnectionId, exception?.Message ?? "clean");

        await base.OnDisconnectedAsync(exception);
    }

    // ── Klient → Server ────────────────────────────────────────────────────

    /// <summary>
    /// Klienten begär explicit grupptillhörighet.
    /// Validerar att klienten bara kan gå med i sin egen org.
    /// </summary>
    public async Task JoinOrgGroupAsync(string orgIdStr)
    {
        if (!Guid.TryParse(orgIdStr, out var requestedOrgId))
        {
            await Clients.Caller.SendAsync("Error", "Invalid organization ID format.");
            return;
        }

        var callerOrgId = GetOrgId();
        if (callerOrgId is null || callerOrgId != requestedOrgId)
        {
            // Förhindra cross-tenant subscriptions
            await Clients.Caller.SendAsync("Error", "Access denied to this organization.");
            _logger.LogWarning(
                "Cross-org group join attempt. Caller={Caller} Requested={Req}",
                callerOrgId, requestedOrgId);
            return;
        }

        var group = OrgGroupName(requestedOrgId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await Clients.Caller.SendAsync("JoinedGroup", group);

        _logger.LogDebug(
            "Client {Id} joined org group {Group}", Context.ConnectionId, group);
    }

    public async Task LeaveOrgGroupAsync(string orgIdStr)
    {
        if (Guid.TryParse(orgIdStr, out var orgId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, OrgGroupName(orgId));
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private Guid? GetOrgId()
    {
        var raw = Context.User?.FindFirst(SynthtaxClaimTypes.OrganizationId)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    internal static string OrgGroupName(Guid orgId) =>
        $"org:{orgId:N}";
}

// ═══════════════════════════════════════════════════════════════════════════
// IAnalysisHubPublisher  — server-side publish-gränssnitt
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Injiceras i Orchestrator (Fas 3), SyncEngine och bakgrundstjänster
/// för att pusha events till anslutna VSIX-klienter.
/// Registreras som Scoped i DI.
/// </summary>
public interface IAnalysisHubPublisher
{
    /// <summary>Pusha AnalysisUpdated till alla klienter i en orgs grupp.</summary>
    Task PublishAnalysisUpdatedAsync(Guid orgId, AnalysisUpdatedEvent payload, CancellationToken ct = default);

    /// <summary>Pusha IssueCreated för ett enskilt nytt issue.</summary>
    Task PublishIssueCreatedAsync(Guid orgId, IssueCreatedEvent payload, CancellationToken ct = default);

    /// <summary>Pusha IssueClosed när ett issue auto-stängs.</summary>
    Task PublishIssueClosedAsync(Guid orgId, IssueClosedEvent payload, CancellationToken ct = default);

    /// <summary>Pusha HealthScoreUpdated när poängen ändras.</summary>
    Task PublishHealthScoreUpdatedAsync(Guid orgId, HealthScoreUpdatedEvent payload, CancellationToken ct = default);
}

/// <summary>
/// IHubContext-baserad implementation av <see cref="IAnalysisHubPublisher"/>.
/// Registreras med <c>services.AddScoped&lt;IAnalysisHubPublisher, AnalysisHubPublisher&gt;()</c>.
/// </summary>
public sealed class AnalysisHubPublisher : IAnalysisHubPublisher
{
    private readonly IHubContext<AnalysisHub> _hub;
    private readonly ILogger<AnalysisHubPublisher> _logger;

    public AnalysisHubPublisher(
        IHubContext<AnalysisHub> hub,
        ILogger<AnalysisHubPublisher> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public async Task PublishAnalysisUpdatedAsync(
        Guid orgId, AnalysisUpdatedEvent payload, CancellationToken ct = default)
    {
        var group = AnalysisHub.OrgGroupName(orgId);
        await _hub.Clients.Group(group)
            .SendAsync(HubMethodNames.AnalysisUpdated, payload, ct);

        _logger.LogInformation(
            "Pushed AnalysisUpdated → org {Org}: {New} new, {Closed} closed, score={Score:F1}",
            orgId, payload.NewIssueCount, payload.ClosedIssueCount, payload.HealthScore);
    }

    public async Task PublishIssueCreatedAsync(
        Guid orgId, IssueCreatedEvent payload, CancellationToken ct = default)
    {
        await _hub.Clients.Group(AnalysisHub.OrgGroupName(orgId))
            .SendAsync(HubMethodNames.IssueCreated, payload, ct);
    }

    public async Task PublishIssueClosedAsync(
        Guid orgId, IssueClosedEvent payload, CancellationToken ct = default)
    {
        await _hub.Clients.Group(AnalysisHub.OrgGroupName(orgId))
            .SendAsync(HubMethodNames.IssueClosed, payload, ct);
    }

    public async Task PublishHealthScoreUpdatedAsync(
        Guid orgId, HealthScoreUpdatedEvent payload, CancellationToken ct = default)
    {
        await _hub.Clients.Group(AnalysisHub.OrgGroupName(orgId))
            .SendAsync(HubMethodNames.HealthScoreUpdated, payload, ct);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Program.cs-tillägg (snippet — ej komplett fil)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Visar hur AnalysisHub registreras i Program.cs.
/// <code>
///   // I builder.Services-blocket:
///   builder.Services.AddSignalR(opts =>
///   {
///       opts.EnableDetailedErrors = builder.Environment.IsDevelopment();
///       opts.MaximumReceiveMessageSize = 512 * 1024; // 512 KB
///   });
///   builder.Services.AddScoped&lt;IAnalysisHubPublisher, AnalysisHubPublisher&gt;();
///
///   // I app.MapXxx-blocket (efter UseAuthentication/UseAuthorization):
///   app.MapHub&lt;AnalysisHub&gt;("/hubs/analysis", opts =>
///   {
///       opts.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
///   });
/// </code>
/// </summary>
internal static class ProgramCsSnippet { }
