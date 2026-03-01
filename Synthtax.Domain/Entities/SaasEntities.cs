using Synthtax.Domain.Enums;

namespace Synthtax.Domain.Entities;

// ═══════════════════════════════════════════════════════════════════════════
// Organization
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// En organisation (företag/team) i Synthtax SaaS-plattformen.
///
/// <para><b>TenantId-konvention:</b> Organization.Id används direkt som TenantId
/// i <c>Project</c> och <c>BacklogItem</c> — inga separata tenant-tabeller.</para>
/// </summary>
public class Organization : AuditableEntity
{
    public Guid   Id   { get; set; } = Guid.NewGuid();

    /// <summary>Visningsnamn, t.ex. "Acme Corp".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-säker unik identifierare, t.ex. "acme-corp".
    /// Används i API-routes och webhook-URL:er.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Aktiv prenumerationsplan.</summary>
    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Free;

    /// <summary>
    /// Antal köpta licenser (platser). Kan inte överstiga planen MaxLicenses.
    /// Sätts vid köp — OrgAdmin kan bjuda in upp till detta antal.
    /// </summary>
    public int PurchasedLicenses { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    /// <summary>Sätts vid trial-registrering — null om fullbetald.</summary>
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>Fritext för billing/kontaktadress.</summary>
    public string? BillingEmail { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public ICollection<OrganizationMembership> Memberships { get; set; } = [];
    public ICollection<Invitation>             Invitations { get; set; } = [];

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    /// <summary>True om organisationen är i trial och det inte har löpt ut.</summary>
    public bool IsInTrial =>
        TrialEndsAt.HasValue && TrialEndsAt.Value > DateTime.UtcNow;

    /// <summary>True om trial löpt ut och organisationen inte uppgraderat.</summary>
    public bool IsTrialExpired =>
        TrialEndsAt.HasValue && TrialEndsAt.Value <= DateTime.UtcNow
        && Plan == SubscriptionPlan.Free;

    public int ActiveMemberCount =>
        Memberships.Count(m => m.IsActive);
}

// ═══════════════════════════════════════════════════════════════════════════
// OrganizationMembership
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kopplar en <c>ApplicationUser</c> till en <see cref="Organization"/> med en roll.
///
/// <para>En användare kan tillhöra flera organisationer (konsult-scenario),
/// men JWT-tokenen bär bara aktiv organisations ID.</para>
/// </summary>
public class OrganizationMembership : AuditableEntity
{
    public Guid   Id             { get; set; } = Guid.NewGuid();
    public Guid   OrganizationId { get; set; }
    public string UserId         { get; set; } = string.Empty; // FK → ApplicationUser.Id

    public OrgRole Role     { get; set; } = OrgRole.Member;
    public bool    IsActive { get; set; } = true;

    /// <summary>Tidpunkt då användaren accepterade en inbjudan.</summary>
    public DateTime? JoinedAt { get; set; }

    /// <summary>Tidpunkt då användaren inaktiverades av OrgAdmin.</summary>
    public DateTime? DeactivatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Organization Organization { get; set; } = null!;

    public bool IsOrgAdmin => Role == OrgRole.OrgAdmin;
}

// ═══════════════════════════════════════════════════════════════════════════
// Invitation
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Inbjudan skickad av en <c>OrgAdmin</c> till en e-postadress.
///
/// <para><b>Flöde:</b>
/// <list type="number">
///   <item>OrgAdmin skapar Invitation via <c>IInvitationService.InviteAsync</c>.</item>
///   <item>E-post skickas med länk: <c>/join?token={Token}</c>.</item>
///   <item>Mottagaren registrerar sig eller loggar in → <c>AcceptAsync(token)</c>.</item>
///   <item>OrganizationMembership skapas, Invitation markeras Accepted.</item>
/// </list>
/// </para>
/// </summary>
public class Invitation : AuditableEntity
{
    public Guid   Id             { get; set; } = Guid.NewGuid();
    public Guid   OrganizationId { get; set; }

    /// <summary>E-postadress som inbjudningen skickades till.</summary>
    public string Email     { get; set; } = string.Empty;

    /// <summary>Roll som mottagaren tilldelas vid accepterande.</summary>
    public OrgRole TargetRole { get; set; } = OrgRole.Member;

    /// <summary>
    /// Kryptografiskt slumpmässig token (256 bitar, hex-kodad).
    /// Skickas som query-parameter i inbjudningslänken.
    /// </summary>
    public string Token     { get; set; } = string.Empty;

    public InvitationStatus Status     { get; set; } = InvitationStatus.Pending;
    public DateTime         ExpiresAt  { get; set; } = DateTime.UtcNow.AddDays(7);

    public DateTime? AcceptedAt      { get; set; }
    public string?   AcceptedByUserId { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────
    public Organization Organization { get; set; } = null!;

    // ── Hjälpmetoder ──────────────────────────────────────────────────────
    public bool IsExpired => Status == InvitationStatus.Pending && DateTime.UtcNow > ExpiresAt;
    public bool CanBeAccepted => Status == InvitationStatus.Pending && !IsExpired;
}
