namespace Synthtax.Shared.SignalR;

// ═══════════════════════════════════════════════════════════════════════════
// Hub-metodnamn  —  kanoniska strängar för client.On()/SendAsync()
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kanoniska metodnamn för Synthtax SignalR-hub.
/// Används av både backend (<c>Clients.Group().SendAsync(...)</c>)
/// och VSIX-klienten (<c>connection.On(...)</c>) för att undvika magic strings.
/// </summary>
public static class HubMethods
{
    // ── Server → Client (push-events) ─────────────────────────────────────

    /// <summary>
    /// Skickas när en ny analyssession avslutats och backlog har uppdaterats.
    /// Payload: <see cref="AnalysisUpdatedPayload"/>.
    /// </summary>
    public const string AnalysisUpdated = "AnalysisUpdated";

    /// <summary>
    /// Skickas när ett enskilt ärende byter status (t.ex. Resolved av annan teammedlem).
    /// Payload: <see cref="IssueStatusChangedPayload"/>.
    /// </summary>
    public const string IssueStatusChanged = "IssueStatusChanged";

    /// <summary>
    /// Skickas när organisationens licens ändras (plan-uppgradering, trial-utgång).
    /// Payload: <see cref="LicenseChangedPayload"/>.
    /// </summary>
    public const string LicenseChanged = "LicenseChanged";

    /// <summary>
    /// Heartbeat från servern var 30 s. Klienten bekräftar att anslutningen lever.
    /// Payload: <see cref="HeartbeatPayload"/>.
    /// </summary>
    public const string Heartbeat = "Heartbeat";

    // ── Client → Server (anrop) ───────────────────────────────────────────

    /// <summary>Klienten begär att prenumerera på sin organisations events.</summary>
    public const string JoinOrganization = "JoinOrganization";

    /// <summary>Klienten lämnar organisations-gruppen (vid utloggning).</summary>
    public const string LeaveOrganization = "LeaveOrganization";

    /// <summary>Klienten bekräftar ett heartbeat.</summary>
    public const string AcknowledgeHeartbeat = "AcknowledgeHeartbeat";
}

// ═══════════════════════════════════════════════════════════════════════════
// Event-payloads
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Payload för <see cref="HubMethods.AnalysisUpdated"/>.
///
/// <para>Skickas efter att en analyssession avslutats. Innehåller en
/// fullständig diff: nya, lösta och kvarstående issues.</para>
/// </summary>
public sealed record AnalysisUpdatedPayload
{
    /// <summary>Organisations-ID som eventet tillhör.</summary>
    public Guid OrganizationId { get; init; }

    /// <summary>Projekt-ID som analysen kördes på.</summary>
    public Guid ProjectId { get; init; }

    /// <summary>Projektets visningsnamn.</summary>
    public string ProjectName { get; init; } = "";

    /// <summary>Analysessionens ID — kan användas för att hämta detaljer via REST.</summary>
    public Guid SessionId { get; init; }

    /// <summary>Tidpunkt när analysen avslutades (UTC).</summary>
    public DateTime CompletedAt { get; init; }

    // ── Diff-statistik ──────────────────────────────────────────────────

    /// <summary>Antal nya issues som skapades i denna session.</summary>
    public int NewIssuesCount { get; init; }

    /// <summary>Antal issues som automatiskt stängdes (koden fixad).</summary>
    public int ResolvedIssuesCount { get; init; }

    /// <summary>Antal kvarstående öppna issues totalt efter sessionen.</summary>
    public int TotalOpenIssues { get; init; }

    /// <summary>
    /// Kompakt lista med de nya issues. Max 50 — använd REST-API för komplett lista.
    /// Tomt om <see cref="NewIssuesCount"/> är 0.
    /// </summary>
    public IReadOnlyList<IssueSummary> NewIssues { get; init; } = [];

    /// <summary>
    /// Fingerprints på issues som stängdes i denna session.
    /// Används av VSIX för att ta bort squiggles omedelbart.
    /// </summary>
    public IReadOnlyList<string> ResolvedFingerprints { get; init; } = [];

    /// <summary>Uppdaterad hälsopoäng (0–100) efter sessionen.</summary>
    public double HealthScore { get; init; }

    /// <summary>True om CI/CD-triggad session (annars manuell/schemalagd).</summary>
    public bool IsCiCdTriggered { get; init; }
}

/// <summary>Kompakt issue-sammanfattning för hub-payload (ej full BacklogItemDto).</summary>
public sealed record IssueSummary
{
    public Guid   Id          { get; init; }
    public string RuleId      { get; init; } = "";
    public string Severity    { get; init; } = "Medium";
    public string FilePath    { get; init; } = "";
    public int    StartLine   { get; init; }
    public string Message     { get; init; } = "";
    public string? ClassName  { get; init; }
    public string? MemberName { get; init; }
    public string Fingerprint { get; init; } = "";
}

/// <summary>Payload för <see cref="HubMethods.IssueStatusChanged"/>.</summary>
public sealed record IssueStatusChangedPayload
{
    public Guid   OrganizationId { get; init; }
    public Guid   IssueId        { get; init; }
    public string OldStatus      { get; init; } = "";
    public string NewStatus      { get; init; } = "";
    public string ChangedByUser  { get; init; } = "";
    public DateTime ChangedAt    { get; init; }
}

/// <summary>Payload för <see cref="HubMethods.LicenseChanged"/>.</summary>
public sealed record LicenseChangedPayload
{
    public Guid   OrganizationId { get; init; }
    public string OldPlan        { get; init; } = "";
    public string NewPlan        { get; init; } = "";
    public string Message        { get; init; } = "";
}

/// <summary>Payload för <see cref="HubMethods.Heartbeat"/>.</summary>
public sealed record HeartbeatPayload
{
    public DateTime ServerTime    { get; init; } = DateTime.UtcNow;
    public int      ConnectedClients { get; init; }
}
