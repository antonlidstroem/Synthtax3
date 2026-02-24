using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class BacklogItemDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public BacklogStatus Status { get; set; }
    public Priority Priority { get; set; }
    public BacklogCategory Category { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? CreatedByUserName { get; set; }
    public Guid TenantId { get; set; }
    public string? Tags { get; set; }
    public string? LinkedFilePath { get; set; }
}

public class CreateBacklogItemDto
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public BacklogStatus Status { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public BacklogCategory Category { get; set; } = BacklogCategory.Bug;
    public DateTime? Deadline { get; set; }
    public string? Tags { get; set; }
    public string? LinkedFilePath { get; set; }
}

public class UpdateBacklogItemDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public BacklogStatus? Status { get; set; }
    public Priority? Priority { get; set; }
    public BacklogCategory? Category { get; set; }
    public DateTime? Deadline { get; set; }
    public string? Tags { get; set; }
    public string? LinkedFilePath { get; set; }
}

public class BacklogSummaryDto
{
    public int TotalItems { get; set; }
    public int TodoCount { get; set; }
    public int InProgressCount { get; set; }
    public int DoneCount { get; set; }
    public int HighPriorityCount { get; set; }
    public int OverdueCount { get; set; }
}
