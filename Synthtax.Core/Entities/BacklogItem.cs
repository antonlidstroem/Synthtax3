using System.Text.Json;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Entities;

public class BacklogItem : AuditableEntity, ISoftDeletable
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public Guid   ProjectId   { get; set; }
    public string RuleId      { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string? PreviousFingerprints { get; set; }
    public BacklogStatus Status              { get; set; } = BacklogStatus.Open;
    public bool   AutoClosed                 { get; set; }
    public Guid?  AutoClosedInSessionId      { get; set; }
    public Guid?  ReopenedInSessionId        { get; set; }
    public Guid?  LastSeenInSessionId        { get; set; }
    public string? Metadata                  { get; set; }

    // Manual backlog fields
    public string  Title           { get; set; } = string.Empty;
    public string? Description     { get; set; }
    public Priority       Priority { get; set; }
    public BacklogCategory Category { get; set; }
    public DateTime? Deadline      { get; set; }
    public DateTime? CompletedAt   { get; set; }
    public string?   Tags          { get; set; }
    public string?   LinkedFilePath { get; set; }

    public Project? Project { get; set; }

    // ISoftDeletable
    public bool      IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string?   DeletedBy { get; set; }
}

public static class BacklogItemExtensions
{
    public static IReadOnlyList<string> GetFingerprintHistory(this BacklogItem item)
    {
        if (item.PreviousFingerprints is null) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(item.PreviousFingerprints) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static bool HasOrHadFingerprint(this BacklogItem item, string fingerprint) =>
        item.Fingerprint == fingerprint ||
        item.GetFingerprintHistory().Contains(fingerprint, StringComparer.Ordinal);
}
