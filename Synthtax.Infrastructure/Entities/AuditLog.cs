namespace Synthtax.Infrastructure.Entities;

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Vilken åtgärd utfördes, t.ex. "Login", "Export", "CreateUser".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Resurstyp, t.ex. "BacklogItem", "User".</summary>
    public string? ResourceType { get; set; }

    /// <summary>Resursens ID (om tillämpligt).</summary>
    public string? ResourceId { get; set; }

    /// <summary>Extra detaljer i JSON eller fritext.</summary>
    public string? Details { get; set; }

    public string? IpAddress { get; set; }
    public bool Success { get; set; } = true;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Multi-tenancy
    public Guid TenantId { get; set; } = Guid.Empty;

    // FK (nullable – systemhändelser kan sakna användare)
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
}
