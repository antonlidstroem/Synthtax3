using Synthtax.Core.Events;

namespace Synthtax.Core.Interfaces;

/// <summary>
/// Utökar IHubPusher med infrastrukturella metoder (Heartbeat)
/// som applagret inte behöver kanna till.
/// </summary>
public interface ISynthtaxHubPusher : IHubPusher
{
    Task PushHeartbeatAsync(HeartbeatEvent payload, CancellationToken ct = default);
}
