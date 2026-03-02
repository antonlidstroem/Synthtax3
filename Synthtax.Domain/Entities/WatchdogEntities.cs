using Synthtax.Domain.Entities;   // AuditableEntity (Fas 1)

namespace Synthtax.Domain.Entities;

// ═══════════════════════════════════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Kategorin av extern källa som bevakas.</summary>
public enum WatchdogSource
{
    VisualStudio       = 0,   // VS release-kanal
    AiModelClaude      = 1,   // Anthropic model changelog
    AiModelOpenAi      = 2,   // OpenAI model changelog
    NuGetPackage       = 3,   // NuGet-paket som VSIX beror på
    RoslynCompiler     = 4,   // Roslyn SDK-uppdateringar
    GitHubCopilot      = 5,   // Copilot API-ändringar
    Custom             = 99
}

/// <summary>Allvarsgrad för ett watchdog-larm.</summary>
public enum AlertSeverity
{
    /// <summary>Information — ingen omedelbar åtgärd krävs.</summary>
    Info     = 0,
    /// <summary>Varning — bör undersökas inom en vecka.</summary>
    Warning  = 1,
    /// <summary>Kritisk — kräver åtgärd innan nästa VSIX-release.</summary>
    Critical = 2
}

/// <summary>Status för ett larm.</summary>
public enum AlertStatus
{
    /// <summary>Nytt larm — ej bekräftat.</summary>
    New          = 0,
    /// <summary>Bekräftat av super-admin — utredning pågår.</summary>
    Acknowledged = 1,
    /// <summary>Åtgärd genomförd och validerad.</summary>
    Resolved     = 2,
    /// <summary>Ignorerat som irrelevant.</summary>
    Dismissed    = 3
}

// ═══════════════════════════════════════════════════════════════════════════
// WatchdogAlert  — larm från en extern källa
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Representerar ett larm som skapats av <c>WatchdogBackgroundService</c>
/// när en extern förändring detekteras (ny VS-version, nytt AI-modell osv.).
///
/// <para><b>Idempotens:</b>
/// <c>ExternalVersionKey</c> fungerar som unik nyckel per källa.
/// Samma version genererar aldrig ett dubblett-larm.</para>
/// </summary>
public class WatchdogAlert : AuditableEntity
{
    /// <summary>Källan som genererade larmet.</summary>
    public WatchdogSource Source { get; set; }

    /// <summary>Allvarsgrad.</summary>
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;

    /// <summary>Aktuell status.</summary>
    public AlertStatus Status { get; set; } = AlertStatus.New;

    /// <summary>
    /// Extern versionsidentifierare, t.ex. "17.12.0", "claude-opus-4-5", "4.8.0".
    /// Används för idempotens-kontroll.
    /// </summary>
    public string ExternalVersionKey { get; set; } = string.Empty;

    /// <summary>Kort titel, t.ex. "Visual Studio 17.12 released".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Detaljerad beskrivning med länk till release notes.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>URL till officiell release notes / changelog.</summary>
    public string? ReleaseNotesUrl { get; set; }

    /// <summary>
    /// Åtgärdsförslag för plugin-teamet, t.ex.
    /// "Run VSIX compatibility tests against VS 17.12 SDK."
    /// </summary>
    public string? ActionRequired { get; set; }

    /// <summary>
    /// Datum när förändringen publicerades externt (ej CreatedAt för detta larm).
    /// </summary>
    public DateTime ExternalPublishedAt { get; set; }

    /// <summary>Bekräftad av super-admin (UserId).</summary>
    public string? AcknowledgedBy { get; set; }
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>Löst av super-admin.</summary>
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Rådata från externa API:et (serialiserat JSON).
    /// Sparas för felsökning och manuell inspektion.
    /// </summary>
    public string? RawPayloadJson { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
// PluginTelemetry  — anonymiserad telemetri från installerade VSIX
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Anonymiserad hälsodata som VSIX-plugins skickar till backend.
///
/// <para><b>Privacy-design:</b>
/// <list type="bullet">
///   <item>Ingen personuppgift — <c>InstallationId</c> är ett slumpmässigt GUID
///         genererat vid installation, ej kopplat till användare.</item>
///   <item>Ingen maskin-identifiering — OS-version och VS-version rundas av
///         till minor-version (17.10.x → "17.10").</item>
///   <item>Retention: 90 dagar (rensas av bakgrundsjobb).</item>
/// </list>
/// </para>
/// </summary>
public class PluginTelemetry : AuditableEntity
{
    /// <summary>
    /// Slumpmässigt UUID genererat vid VSIX-installation.
    /// Används för att aggregera data per installation utan identifiering.
    /// </summary>
    public Guid InstallationId { get; set; }

    /// <summary>VSIX-version, t.ex. "1.2.3".</summary>
    public string PluginVersion { get; set; } = string.Empty;

    /// <summary>VS-version rundad till minor, t.ex. "17.10".</summary>
    public string VsVersionBucket { get; set; } = string.Empty;

    /// <summary>OS-plattform: "Windows 11", "Windows 10" osv.</summary>
    public string OsPlatform { get; set; } = string.Empty;

    /// <summary>Median-latens för API-anrop under rapportperioden (ms).</summary>
    public double MedianApiLatencyMs { get; set; }

    /// <summary>P95-latens för API-anrop (ms).</summary>
    public double P95ApiLatencyMs { get; set; }

    /// <summary>Antal misslyckade API-anrop under perioden.</summary>
    public int FailedRequestCount { get; set; }

    /// <summary>Totalt antal API-anrop under perioden.</summary>
    public int TotalRequestCount { get; set; }

    /// <summary>Antal Roslyn-analyzer-crash (utan stacktrace — bara räknare).</summary>
    public int AnalyzerCrashCount { get; set; }

    /// <summary>SignalR ansluten procent av perioden (0.0–1.0).</summary>
    public double SignalRUptimeFraction { get; set; }

    /// <summary>Rapportperiod start (UTC, rundad till timme).</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>Rapportperiod slut (UTC, rundad till timme).</summary>
    public DateTime PeriodEnd { get; set; }

    // Beräknade egenskaper
    public double SuccessRate => TotalRequestCount == 0 ? 1.0
        : 1.0 - (double)FailedRequestCount / TotalRequestCount;
    public bool IsHealthy => SuccessRate >= 0.95 && P95ApiLatencyMs < 2000 && AnalyzerCrashCount == 0;
}

// ═══════════════════════════════════════════════════════════════════════════
// WatchdogRun  — protokoll av varje watchdog-körning
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Loggpost för en watchdog-körning (bakgrundsjobb).</summary>
public class WatchdogRun : AuditableEntity
{
    public WatchdogSource Source       { get; set; }
    public bool           Success      { get; set; }
    public string?        ErrorMessage { get; set; }
    public int            NewAlerts    { get; set; }
    public int            DurationMs   { get; set; }
    public DateTime       RanAt        { get; set; } = DateTime.UtcNow;
}
