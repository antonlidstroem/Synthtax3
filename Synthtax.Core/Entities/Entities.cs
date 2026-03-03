using Synthtax.Core.Enums;

namespace Synthtax.Core.Entities;

// ═══════════════════════════════════════════════════════════════════════════
// Rule
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Representerar en analysregel i Synthtax.
/// Populeras automatiskt från plugin-systemet vid appstart via RuleSeedService.
/// RuleId matchar ILanguageRule.RuleId ("CA001", "JAVA003", "WEB008" …).
/// </summary>
public class Rule : AuditableEntity
{
    /// <summary>
    /// Naturlig primärnyckel — sätts av plugin-systemet, aldrig auto-genererad.
    /// Format: två–fyra versaler följt av tre siffror (t.ex. "CA001", "JAVA012").
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    public string   Name            { get; set; } = string.Empty;
    public string   Description     { get; set; } = string.Empty;
    public string   Category        { get; set; } = string.Empty;
    public Severity DefaultSeverity { get; set; } = Severity.Medium;

    /// <summary>Plugin-version senast regeln uppdaterades ("1.2.0").</summary>
    public string Version   { get; set; } = "1.0.0";
    public bool   IsEnabled { get; set; } = true;

    public ICollection<BacklogItem> BacklogItems { get; set; } = [];
}

// ═══════════════════════════════════════════════════════════════════════════
// Project
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Ett kodprojekt som analyseras av Synthtax.
/// Stöder lokala sökvägar och fjärr-repos (GitHub/GitLab/Azure DevOps).
/// </summary>
public class Project : AuditableEntity, ISoftDeletable
{
    public Guid   Id       { get; set; } = Guid.NewGuid();
    public string Name     { get; set; } = string.Empty;

    /// <summary>Lokal sökväg till .sln-filen eller projektkatalogen.</summary>
    public string? PhysicalPath { get; set; }

    /// <summary>Git-URL för kloning (https/ssh). Null = lokalt projekt.</summary>
    public string? RemoteUrl { get; set; }

    public LanguageType LanguageType { get; set; } = LanguageType.Unknown;

    /// <summary>Affärskritikalitet — styr CI/CD-krav och prioritering.</summary>
    public TierLevel TierLevel { get; set; } = TierLevel.Tier3;

    /// <summary>Multi-tenant stöd. Guid.Empty = systemtäckande (admin).</summary>
    public Guid TenantId { get; set; }

    // ── Optimistic Concurrency ─────────────────────────────────────────────
    /// <summary>Hanteras av EF Core. Konfigureras som IsRowVersion() i Fluent API.</summary>
    public byte[] RowVersion { get; set; } = [];

    // ── Soft Delete ────────────────────────────────────────────────────────
    public bool      IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string?   DeletedBy { get; set; }

    public ICollection<AnalysisSession> Sessions     { get; set; } = [];
    public ICollection<BacklogItem>     BacklogItems { get; set; } = [];
}

// ═══════════════════════════════════════════════════════════════════════════
// AnalysisSession
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// En enskild analyskörning av ett projekt.
/// Möjliggör trend-spårning: "förbättrades kodkvaliteten mellan körning A och B?"
/// </summary>
public class AnalysisSession : AuditableEntity
{
    public Guid     Id        { get; set; } = Guid.NewGuid();
    public Guid     ProjectId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Aggregerat kvalitetsindex 0–100. Beräknas av ScoringService.</summary>
    public double OverallScore { get; set; }

    /// <summary>Issues som är nya sedan föregående session.</summary>
    public int NewIssues { get; set; }

    /// <summary>Issues vars Fingerprint inte längre finns i koden sedan föregående session.</summary>
    public int ResolvedIssues { get; set; }

    /// <summary>Totalt antal aktiva (Open/Acknowledged/InProgress) issues vid körningen.</summary>
    public int TotalIssues { get; set; }

    /// <summary>Analystid i millisekunder. Används för prestandaövervakning.</summary>
    public long DurationMs { get; set; }

    /// <summary>Git-commit SHA — korrelerar issues mot exakt kodversion.</summary>
    public string? CommitSha { get; set; }

    /// <summary>Serialiserad JSON-array med eventuella fel under körningen.</summary>
    public string? ErrorsJson { get; set; }

    public Project Project { get; set; } = null!;
}

// ═══════════════════════════════════════════════════════════════════════════
// BacklogItem
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Ett backlog-ärende för en specifik kodissue i ett projekt.
/// </summary>
public class BacklogItem : AuditableEntity, ISoftDeletable
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public Guid   ProjectId { get; set; }
    public string RuleId    { get; set; } = string.Empty;

    public bool AutoClosed             { get; set; }
    public Guid? AutoClosedInSessionId { get; set; }
    public Guid? ReopenedInSessionId   { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public BacklogStatus Status { get; set; } = BacklogStatus.Open;

    public Severity? SeverityOverride { get; set; }

    public string? Metadata { get; set; }

    public Guid? LastSeenInSessionId { get; set; }

    public Guid TenantId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // ── Soft Delete ────────────────────────────────────────────────────────
    public bool      IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string?   DeletedBy { get; set; }

    public Project  Project  { get; set; } = null!;
    public Rule     Rule     { get; set; } = null!;
    public ICollection<Comment> Comments { get; set; } = [];

    public Severity EffectiveSeverity =>
        SeverityOverride ?? Rule?.DefaultSeverity ?? Severity.Medium;
}

// ═══════════════════════════════════════════════════════════════════════════
// Comment
// ═══════════════════════════════════════════════════════════════════════════

public class Comment : AuditableEntity
{
    public Guid    Id            { get; set; } = Guid.NewGuid();
    public Guid    BacklogItemId { get; set; }
    public string  Text          { get; set; } = string.Empty;
    public string  UserId        { get; set; } = string.Empty;
    public string? UserName      { get; set; }
    public DateTime? EditedAt   { get; set; }

    public BacklogItem BacklogItem { get; set; } = null!;
}
