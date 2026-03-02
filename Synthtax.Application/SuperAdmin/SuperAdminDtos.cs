using Synthtax.Domain.Entities;

namespace Synthtax.Application.SuperAdmin;

// ═══════════════════════════════════════════════════════════════════════════
// Organisation-DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Fullständig vy av en organisation för super-admin-panelen.</summary>
public sealed record OrgAdminDto
{
    public Guid     Id                { get; init; }
    public string   Name              { get; init; } = "";
    public string   Slug              { get; init; } = "";
    public string   Plan              { get; init; } = "";
    public int      PurchasedLicenses { get; init; }
    public int      ActiveMembers     { get; init; }
    public int      TotalProjects     { get; init; }
    public int      OpenIssues        { get; init; }
    public bool     IsActive          { get; init; }
    public bool     IsOnTrial         { get; init; }
    public DateTime? TrialEndsAt      { get; init; }
    public string?  BillingEmail      { get; init; }
    public DateTime CreatedAt         { get; init; }
    public DateTime? LastAnalyzedAt   { get; init; }
    public double   HealthScore       { get; init; }

    /// <summary>Aktiverade feature-flaggor, t.ex. ["FuzzyMatching","CiCd"].</summary>
    public IReadOnlyList<string> EnabledFeatures { get; init; } = [];
}

/// <summary>Paginerat svar för org-listan.</summary>
public sealed record OrgListResponse
{
    public IReadOnlyList<OrgAdminDto> Items      { get; init; } = [];
    public int                        TotalCount { get; init; }
    public int                        Page       { get; init; }
    public int                        PageSize   { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / Math.Max(PageSize, 1));
}

/// <summary>Begäran: skapa ny organisation.</summary>
public sealed record CreateOrgRequest
{
    public required string Name              { get; init; }
    public required string Slug              { get; init; }
    public required string Plan              { get; init; }  // "Free"|"Starter"|"Professional"|"Enterprise"
    public required int    PurchasedLicenses { get; init; }
    public          string? BillingEmail     { get; init; }
    public          bool    StartOnTrial     { get; init; }
    public          IReadOnlyList<string> EnabledFeatures { get; init; } = [];
}

/// <summary>Begäran: uppdatera organisation (alla fält valfria = partial update).</summary>
public sealed record UpdateOrgRequest
{
    public string?  Name              { get; init; }
    public string?  Plan              { get; init; }
    public int?     PurchasedLicenses { get; init; }
    public string?  BillingEmail      { get; init; }
    public bool?    IsActive          { get; init; }
    public IReadOnlyList<string>? EnabledFeatures { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// Watchdog Alert-DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Alert-vy för dashboarden.</summary>
public sealed record AlertDto
{
    public Guid          Id                   { get; init; }
    public string        Source               { get; init; } = "";
    public string        Severity             { get; init; } = "";
    public string        Status               { get; init; } = "";
    public string        Title                { get; init; } = "";
    public string        Description          { get; init; } = "";
    public string?       ReleaseNotesUrl      { get; init; }
    public string?       ActionRequired       { get; init; }
    public string        ExternalVersionKey   { get; init; } = "";
    public DateTime      ExternalPublishedAt  { get; init; }
    public DateTime      CreatedAt            { get; init; }
    public string?       AcknowledgedBy       { get; init; }
    public DateTime?     AcknowledgedAt       { get; init; }
    public bool          IsNew                => Status == "New";
    public bool          RequiresAction       => Severity == "Critical" && Status == "New";
}

/// <summary>Paginerat svar för alert-listan.</summary>
public sealed record AlertListResponse
{
    public IReadOnlyList<AlertDto> Items      { get; init; } = [];
    public int                     TotalCount { get; init; }
    public int                     NewCount   { get; init; }
    public int                     CriticalCount { get; init; }
}

/// <summary>Begäran: bekräfta eller lös ett larm.</summary>
public sealed record UpdateAlertStatusRequest
{
    public required string NewStatus { get; init; }  // "Acknowledged"|"Resolved"|"Dismissed"
    public string? Comment { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// Telemetri-DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Begäran från VSIX-plugin för att rapportera hälsometrik.</summary>
public sealed record TelemetryIngestRequest
{
    /// <summary>Slumpmässigt GUID genererat vid installation (ingen persondata).</summary>
    public required Guid   InstallationId       { get; init; }
    public required string PluginVersion         { get; init; }
    public required string VsVersionBucket       { get; init; }  // "17.10"
    public required string OsPlatform            { get; init; }  // "Windows 11"
    public required double MedianApiLatencyMs    { get; init; }
    public required double P95ApiLatencyMs       { get; init; }
    public required int    FailedRequestCount    { get; init; }
    public required int    TotalRequestCount     { get; init; }
    public required int    AnalyzerCrashCount    { get; init; }
    public required double SignalRUptimeFraction  { get; init; }
    public required DateTime PeriodStart          { get; init; }
    public required DateTime PeriodEnd            { get; init; }
}

/// <summary>Global hälsoöversikt för super-admin-dashboarden.</summary>
public sealed record GlobalHealthDto
{
    /// <summary>Totalt antal aktiva installationer (unika InstallationId senaste 7 dagar).</summary>
    public int    ActiveInstallations    { get; init; }

    /// <summary>Genomsnittlig API-latens (median) för alla installationer.</summary>
    public double AvgMedianLatencyMs     { get; init; }

    /// <summary>P95-latens aggregerat.</summary>
    public double AvgP95LatencyMs        { get; init; }

    /// <summary>Andel installationer utan fel under perioden.</summary>
    public double HealthyInstallFraction { get; init; }

    /// <summary>Totalt antal Roslyn-crashes under perioden.</summary>
    public int    TotalAnalyzerCrashes   { get; init; }

    /// <summary>Genomsnittlig SignalR-uptime.</summary>
    public double AvgSignalRUptime       { get; init; }

    /// <summary>Version-distribution: {"1.2.0": 145, "1.1.9": 23, ...}.</summary>
    public IReadOnlyDictionary<string, int> VersionDistribution { get; init; }
        = new Dictionary<string, int>();

    /// <summary>VS-version-distribution: {"17.10": 200, "17.9": 50, ...}.</summary>
    public IReadOnlyDictionary<string, int> VsVersionDistribution { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Daglig aktiva installationer senaste 14 dagar (datum → antal).</summary>
    public IReadOnlyList<DailyActiveDto> DailyActive { get; init; } = [];

    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>Daglig aktiva installationer — för sparkline-diagram.</summary>
public sealed record DailyActiveDto(DateTime Date, int Count);

// ═══════════════════════════════════════════════════════════════════════════
// Watchdog-status-DTO
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Status-vy för varje watchdog-källa i dashboarden.</summary>
public sealed record WatchdogStatusDto
{
    public string    Source        { get; init; } = "";
    public bool      IsEnabled     { get; init; }
    public DateTime? LastRunAt     { get; init; }
    public bool?     LastRunOk     { get; init; }
    public string?   LastError     { get; init; }
    public int       NewAlertsLast24h { get; init; }
    public string?   CurrentVersion   { get; init; }  // Senast detekterade version
    public DateTime? NextScheduledRun { get; init; }
}

/// <summary>Svaret på GET /admin/watchdog/status.</summary>
public sealed record WatchdogStatusResponse
{
    public IReadOnlyList<WatchdogStatusDto> Sources         { get; init; } = [];
    public int                              TotalNewAlerts  { get; init; }
    public int                              TotalCritical   { get; init; }
}
