using Microsoft.AspNetCore.Identity;
using Synthtax.Domain.Entities;

namespace Synthtax.Infrastructure.Entities;

/// <summary>
/// Utökar ASP.NET Core Identity-användaren med Synthtax-specifika fält.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public Guid TenantId { get; set; } = Guid.Empty;

    public ICollection<OrganizationMembership> Memberships { get; set; } = [];

    /// <summary>
    /// Kommaseparerad lista med modulnamn som användaren har åtkomst till.
    /// Tom = alla moduler tillgängliga.
    /// </summary>
    public string? AllowedModules { get; set; }

    // Navigation
    public UserPreference? Preferences { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<BacklogItem> BacklogItems { get; set; } = new List<BacklogItem>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
