using Synthtax.Core.Enums;

namespace Synthtax.Infrastructure.Entities;

public class BacklogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public BacklogStatus Status { get; set; } = BacklogStatus.Todo;
    public Priority Priority { get; set; } = Priority.Medium;
    public BacklogCategory Category { get; set; } = BacklogCategory.Bug;
    public DateTime? Deadline { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Tags { get; set; }
    public string? LinkedFilePath { get; set; }

    // Multi-tenancy
    public Guid TenantId { get; set; } = Guid.Empty;

    // FK
    public string CreatedByUserId { get; set; } = string.Empty;
    public ApplicationUser CreatedByUser { get; set; } = null!;
}
