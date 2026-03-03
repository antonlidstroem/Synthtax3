namespace Synthtax.Application.SuperAdmin.DTOs;

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

public sealed record AlertListResponse
{
    public IReadOnlyList<AlertDto> Items         { get; init; } = [];
    public int                     TotalCount    { get; init; }
    public int                     NewCount      { get; init; }
    public int                     CriticalCount { get; init; }
}

public sealed record AlertSummaryDto
{
    public int NewCount      { get; init; }
    public int CriticalCount { get; init; }
    public int WarningCount  { get; init; }
    public int TotalOpen     { get; init; }
}

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

public sealed record WatchdogStatusResponse
{
    public IReadOnlyList<WatchdogStatusDto> Sources        { get; init; } = [];
    public int                              TotalNewAlerts { get; init; }
    public int                              TotalCritical  { get; init; }
}

public sealed record GlobalHealthDto
{
    public int                              ActiveInstallations    { get; init; }
    public double                           AvgMedianLatencyMs     { get; init; }
    public double                           AvgP95LatencyMs        { get; init; }
    public double                           HealthyInstallFraction { get; init; }
    public int                              TotalAnalyzerCrashes   { get; init; }
    public double                           AvgSignalRUptime       { get; init; }
    public IReadOnlyDictionary<string, int> VersionDistribution    { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> VsVersionDistribution  { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<DailyActiveDto>    DailyActive            { get; init; } = [];
    public DateTime                         GeneratedAt            { get; init; } = DateTime.UtcNow;
}

public sealed record DailyActiveDto(DateTime Date, int ActiveInstallations);

public sealed record TelemetryIngestRequest
{
    public required Guid     InstallationId        { get; init; }
    public required string   PluginVersion         { get; init; }
    public required string   VsVersionBucket       { get; init; }
    public required string   OsPlatform            { get; init; }
    public required DateTime PeriodStart           { get; init; }
    public required DateTime PeriodEnd             { get; init; }
    public double            MedianApiLatencyMs     { get; init; }
    public double            P95ApiLatencyMs        { get; init; }
    public int               FailedRequestCount     { get; init; }
    public int               TotalRequestCount      { get; init; }
    public int               AnalyzerCrashCount     { get; init; }
    public double            SignalRUptimeFraction   { get; init; }
}
