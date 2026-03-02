namespace Synthtax.Vsix.Services;

// ═══════════════════════════════════════════════════════════════════════════
// RealtimeConnectionState  — VSIX-sidan av anslutningsstatus
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Tillståndsmaskin för SignalR-anslutningens livscykel.</summary>
public enum RealtimeConnectionState
{
    /// <summary>Ej initialiserad — tjänsten har inte startat.</summary>
    Disconnected  = 0,

    /// <summary>Försöker upprätta anslutning (första gången eller återanslutning).</summary>
    Connecting    = 1,

    /// <summary>Ansluten och tar emot events.</summary>
    Connected     = 2,

    /// <summary>
    /// Tillfälligt bortkopplad — automatisk återanslutning pågår.
    /// Visas diskret i statusfältet.
    /// </summary>
    Reconnecting  = 3,

    /// <summary>
    /// Permanent bortkopplad — max antal återanslutningsförsök uppnått
    /// eller token ogiltig. Kräver manuell åtgärd.
    /// </summary>
    Failed        = 4
}

/// <summary>
/// Immutabelt snapshot av anslutningsstatus.
/// Publiceras via <see cref="SynthtaxRealtimeService.ConnectionStateChanged"/>.
/// </summary>
public sealed record ConnectionStateSnapshot
{
    public RealtimeConnectionState State       { get; init; }
    public DateTime                ChangedAt   { get; init; } = DateTime.UtcNow;
    public string?                 ErrorMessage { get; init; }
    public int                     RetryAttempt { get; init; }
    public TimeSpan?               NextRetryIn  { get; init; }

    // ── Display-helpers ────────────────────────────────────────────────────

    /// <summary>Kort statustext för VS statusfält (≤ 60 tecken).</summary>
    public string StatusBarText => State switch
    {
        RealtimeConnectionState.Disconnected => "Synthtax: Offline",
        RealtimeConnectionState.Connecting   => "Synthtax: Ansluter…",
        RealtimeConnectionState.Connected    => "Synthtax: Live ●",
        RealtimeConnectionState.Reconnecting => NextRetryIn.HasValue
            ? $"Synthtax: Återansluter ({NextRetryIn.Value.TotalSeconds:F0}s)…"
            : "Synthtax: Återansluter…",
        RealtimeConnectionState.Failed       => "Synthtax: Anslutning misslyckades",
        _                                    => "Synthtax"
    };

    /// <summary>Tooltip-text för statusfälts-ikonen.</summary>
    public string ToolTipText => State switch
    {
        RealtimeConnectionState.Connected    => $"Synthtax realtidsuppdateringar aktiva sedan {ChangedAt.ToLocalTime():HH:mm}",
        RealtimeConnectionState.Reconnecting => $"Försök {RetryAttempt}: {ErrorMessage ?? "Anslutningen tappades"}",
        RealtimeConnectionState.Failed       => $"Permanent fel: {ErrorMessage}. Logga in igen.",
        _                                    => "Synthtax Code Quality — realtidsstatus"
    };

    /// <summary>True om anslutningsfel ska visas prominent (inte bara statusfält).</summary>
    public bool RequiresUserAction => State == RealtimeConnectionState.Failed;

    public static ConnectionStateSnapshot Disconnected() => new()
        { State = RealtimeConnectionState.Disconnected };
    public static ConnectionStateSnapshot Connecting()   => new()
        { State = RealtimeConnectionState.Connecting };
    public static ConnectionStateSnapshot Connected()    => new()
        { State = RealtimeConnectionState.Connected };
    public static ConnectionStateSnapshot Reconnecting(int attempt, TimeSpan? nextIn, string? err) => new()
        { State = RealtimeConnectionState.Reconnecting, RetryAttempt = attempt,
          NextRetryIn = nextIn, ErrorMessage = err };
    public static ConnectionStateSnapshot Failed(string? err) => new()
        { State = RealtimeConnectionState.Failed, ErrorMessage = err };
}

// ═══════════════════════════════════════════════════════════════════════════
// Händelseargument — skickas via C#-events från SynthtaxRealtimeService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Händelse: AnalysisUpdated-payload mottagen.</summary>
public sealed class AnalysisUpdatedEventArgs : EventArgs
{
    public required Synthtax.Realtime.Contracts.AnalysisUpdatedEvent Payload { get; init; }
}

/// <summary>Händelse: ett enskilt nytt issue.</summary>
public sealed class IssueCreatedEventArgs : EventArgs
{
    public required Synthtax.Realtime.Contracts.IssueCreatedEvent Payload { get; init; }
}

/// <summary>Händelse: ett issue auto-stängt.</summary>
public sealed class IssueClosedEventArgs : EventArgs
{
    public required Synthtax.Realtime.Contracts.IssueClosedEvent Payload { get; init; }
}

/// <summary>Händelse: hälsopoäng förändrad.</summary>
public sealed class HealthScoreUpdatedEventArgs : EventArgs
{
    public required Synthtax.Realtime.Contracts.HealthScoreUpdatedEvent Payload { get; init; }
}
