using Synthtax.Core.Enums;

namespace Synthtax.Core.Entities;

// BacklogItem är definierad i BacklogItem.cs — INTE här.

// ═══════════════════════════════════════════════════════════════════════════
// Rule
// ═══════════════════════════════════════════════════════════════════════════
public class Rule : AuditableEntity
{
    public string RuleId          { get; set; } = string.Empty;
    public string Name            { get; set; } = string.Empty;
    public string Description     { get; set; } = string.Empty;
    public string Category        { get; set; } = string.Empty;
    public Severity DefaultSeverity { get; set; } = Severity.Medium;
    public string Version         { get; set; } = "1.0.0";
    public bool   IsEnabled       { get; set; } = true;

    public ICollection<BacklogItem> BacklogItems { get; set; } = [];
}

// ═══════════════════════════════════════════════════════════════════════════
// Project
// ═══════════════════════════════════════════════════════════════════════════
public class Project : AuditableEntity, ISoftDeletable
{
    public Guid   Id            { get; set; } = Guid.NewGuid();
    public string Name          { get; set; } = string.Empty;
    public string? PhysicalPath { get; set; }
    public string? RemoteUrl    { get; set; }

    public LanguageType LanguageType { get; set; } = LanguageType.Unknown;
    public TierLevel    TierLevel    { get; set; } = TierLevel.Tier3;
    public Guid         TenantId     { get; set; }

    public byte[]    RowVersion { get; set; } = [];
    public bool      IsDeleted  { get; set; }
    public DateTime? DeletedAt  { get; set; }
    public string?   DeletedBy  { get; set; }

    public ICollection<AnalysisSession> Sessions     { get; set; } = [];
    public ICollection<BacklogItem>     BacklogItems { get; set; } = [];
}

// ═══════════════════════════════════════════════════════════════════════════
// Comment
// ═══════════════════════════════════════════════════════════════════════════
public class Comment : AuditableEntity
{
    public Guid      Id            { get; set; } = Guid.NewGuid();
    public Guid      BacklogItemId { get; set; }
    public string    Text          { get; set; } = string.Empty;
    public string    UserId        { get; set; } = string.Empty;
    public string?   UserName      { get; set; }
    public DateTime? EditedAt      { get; set; }

    public BacklogItem BacklogItem { get; set; } = null!;
}
