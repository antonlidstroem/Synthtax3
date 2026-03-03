using Synthtax.Application.Services;
using Synthtax.Realtime.Contracts;

namespace Synthtax.Backend.Hubs;

/// <summary>
/// Utökar IHubPusher med infrastrukturella metoder som
/// Heartbeat, vilka applagret inte behöver känna till.
/// </summary>
public interface ISynthtaxHubPusher : IHubPusher
{
    Task PushHeartbeatAsync(HeartbeatEvent payload, CancellationToken ct = default);
}
