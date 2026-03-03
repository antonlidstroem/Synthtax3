using Synthtax.Core.Enums;

namespace Synthtax.Core.Entities;

public class BacklogItem : AuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string? PreviousFingerprints { get; set; }
    public BacklogStatus Status { get; set; } = BacklogStatus.Open;
    public Severity? SeverityOverride { get; set; } // Tillagd
    public bool AutoClosed { get; set; }
    public Guid? AutoClosedInSessionId { get; set; }
    public Guid? ReopenedInSessionId { get; set; }
    public Guid? LastSeenInSessionId { get; set; }
    public string? Metadata { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Priority Priority { get; set; }
    public BacklogCategory Category { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; } // Tillagd
    public DateTime? CompletedAt { get; set; }
    public string? Tags { get; set; }
    public string? LinkedFilePath { get; set; }

    public byte[] RowVersion { get; set; } = []; // För Concurrency
    public Project? Project { get; set; }
    public Rule? Rule { get; set; } // Tillagd
    public ICollection<Comment> Comments { get; set; } = new List<Comment>(); // Tillagd

    public Severity EffectiveSeverity => SeverityOverride ?? Rule?.DefaultSeverity ?? Severity.Low;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}