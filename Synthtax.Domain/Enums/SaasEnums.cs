namespace Synthtax.Domain.Enums;

// ═══════════════════════════════════════════════════════════════════════════
// SubscriptionPlan
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Organisationens prenumerationsplan.
/// Styr vilka gränsvärden som gäller via <see cref="Synthtax.Domain.ValueObjects.LicenseLimits"/>.
/// </summary>
public enum SubscriptionPlan
{
    /// <summary>Gratis-plan — begränsat för enskilda dev/proof-of-concept.</summary>
    Free         = 0,

    /// <summary>Starter — litet team, begränsat antal projekt.</summary>
    Starter      = 1,

    /// <summary>Professional — mellanstort team med fulla Tier1–2-projekt.</summary>
    Professional = 2,

    /// <summary>Enterprise — obegränsat, anpassade SLA:er.</summary>
    Enterprise   = 99
}

// ═══════════════════════════════════════════════════════════════════════════
// OrgRole
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Roll inom en organisation.
/// Separerat från globala ASP.NET Identity-roller (Admin/User).
/// </summary>
public enum OrgRole
{
    /// <summary>Vanlig teammedlem — kan analysera och se backlog.</summary>
    Member   = 0,

    /// <summary>
    /// Organisations-admin — kan bjuda in/ta bort membres, ändra plan-inställningar
    /// och administrera projekt. Kan INTE ändra global prenumerationsplan.
    /// </summary>
    OrgAdmin = 1
}

// ═══════════════════════════════════════════════════════════════════════════
// InvitationStatus
// ═══════════════════════════════════════════════════════════════════════════

public enum InvitationStatus
{
    Pending  = 0,
    Accepted = 1,
    Expired  = 2,
    Revoked  = 3
}
