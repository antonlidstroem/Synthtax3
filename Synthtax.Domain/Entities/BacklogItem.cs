using Synthtax.Domain.Enums;

namespace Synthtax.Domain.Entities;

public class BacklogItem : AuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;

    // FAS 4: Historik för fingerprints (lagras som JSON-sträng)
    public string? PreviousFingerprints { get; set; }

    public BacklogStatus Status { get; set; } = BacklogStatus.Open;
    public Severity? SeverityOverride { get; set; }
    public string? Metadata { get; set; }

    // FAS 3: Auto-stängning logik
    public bool AutoClosed { get; set; }
    public Guid? AutoClosedInSessionId { get; set; }
    public Guid? ReopenedInSessionId { get; set; }
    public Guid? LastSeenInSessionId { get; set; }

    public Guid TenantId { get; set; }
    public byte[] RowVersion { get; set; } = [];

    // Soft Delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
    public Rule Rule { get; set; } = null!;
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public Severity EffectiveSeverity => SeverityOverride ?? Rule?.DefaultSeverity ?? Severity.Medium;
}