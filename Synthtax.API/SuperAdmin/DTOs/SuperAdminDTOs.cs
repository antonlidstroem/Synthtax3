namespace Synthtax.API.SuperAdmin.DTOs;

// ── Alert DTOs ─────────────────────────────────────────────────────────────

/// <summary>Standardvy för ett watchdog-larm.</summary>
public record AlertDto
{
    public Guid      Id                  { get; init; }
    public string    Source              { get; init; } = string.Empty;
    public string    Severity            { get; init; } = string.Empty;
    public string    Status              { get; init; } = string.Empty;
    public string    Title               { get; init; } = string.Empty;
    public string?   Description         { get; init; }
    public string?   ReleaseNotesUrl     { get; init; }
    public string?   ActionRequired      { get; init; }
    public string?   ExternalVersionKey  { get; init; }
    public DateTime? ExternalPublishedAt { get; init; }
    public DateTime  CreatedAt           { get; init; }
    public string?   AcknowledgedBy      { get; init; }
    public DateTime? AcknowledgedAt      { get; init; }
}

/// <summary>Paginerad lista med larm.</summary>
public sealed record AlertListResponse
{
    public IReadOnlyList<AlertDto> Items      { get; init; } = [];
    public int                     TotalCount { get; init; }
    public int                     Page       { get; init; }
    public int                     PageSize   { get; init; }
}

/// <summary>Request-body för statuspatch på larm.</summary>
public sealed record UpdateAlertStatusRequest
{
    public string  NewStatus { get; init; } = string.Empty;
    public string? Comment   { get; init; }
}

// ── Telemetry DTOs ─────────────────────────────────────────────────────────

/// <summary>Payload som VSIX-plugin skickar vid hälsorapportering.</summary>
public sealed record TelemetryIngestRequest
{
    public string  PluginVersion  { get; init; } = string.Empty;
    public string  VsVersion      { get; init; } = string.Empty;
    public string  OsVersion      { get; init; } = string.Empty;
    public string? AnonymousId    { get; init; }
    public bool    IsActive       { get; init; }
    public DateTime ReportedAt   { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Metrics { get; init; } = new();
}

/// <summary>Aggregerad global hälsostatus från telemetridata.</summary>
public sealed record GlobalHealthDto
{
    public int                              ActiveInstallations   { get; init; }
    public int                              ReportingLast7Days    { get; init; }
    public IReadOnlyDictionary<string, int> VersionDistribution   { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> VsVersionDistribution { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> OsDistribution        { get; init; }
        = new Dictionary<string, int>();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

// ── Watchdog DTOs ──────────────────────────────────────────────────────────

/// <summary>Status för en enskild watchdog-källa.</summary>
public sealed record WatchdogStatusDto
{
    public required string   Source           { get; init; }
    public required bool     IsEnabled        { get; init; }
    public DateTime?         LastRunAt        { get; init; }
    public bool?             LastRunOk        { get; init; }
    public string?           LastError        { get; init; }
    public int               NewAlertsLast24h { get; init; }
    public DateTime?         NextScheduledRun { get; init; }
}

/// <summary>Samlad status för alla watchdog-källor.</summary>
public sealed record WatchdogStatusResponse
{
    public IReadOnlyList<WatchdogStatusDto> Sources        { get; init; } = [];
    public int                              TotalNewAlerts { get; init; }
    public int                              TotalCritical  { get; init; }
}
