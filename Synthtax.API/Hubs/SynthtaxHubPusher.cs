using Microsoft.AspNetCore.SignalR;
using Synthtax.Realtime.Contracts;

namespace Synthtax.API.Hubs;

public sealed class SynthtaxHubPusher : ISynthtaxHubPusher
{
    private readonly IHubContext<SynthtaxHub> _hub;

    public SynthtaxHubPusher(IHubContext<SynthtaxHub> hub)
        => _hub = hub;

    public Task PushAnalysisUpdatedAsync(
        AnalysisUpdatedEvent payload,
        CancellationToken    ct = default)
        => _hub.Clients
               .Group(OrgGroup(payload.OrganizationId))
               .SendAsync(HubMethodNames.AnalysisUpdated, payload, ct);

    public Task PushIssueStatusChangedAsync(
        IssueStatusChangedEvent payload,
        CancellationToken       ct = default)
        => _hub.Clients
               .Group(OrgGroup(payload.OrganizationId))
               .SendAsync(HubMethodNames.IssueStatusChanged, payload, ct);

    
