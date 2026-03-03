namespace Synthtax.Vsix.SignalR;

/// <summary>
/// Synthtax-specifikt anslutningstillstånd.
/// Utökar SignalRs inbyggda tillstånd med AuthError.
/// </summary>
public enum HubConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthError
}

/// <summary>Mer detaljerad snapshot för StatusBarRealtimeService.</summary>
public sealed record ConnectionStateSnapshot(
    RealtimeConnectionState State,
    string?                 ErrorMessage = null,
    TimeSpan?               NextRetryIn  = null);

public enum RealtimeConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}
