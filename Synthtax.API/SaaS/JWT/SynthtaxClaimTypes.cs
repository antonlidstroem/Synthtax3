using System.Security.Claims;
using Synthtax.Domain.Enums;

namespace Synthtax.API.SaaS.JWT;

// ═══════════════════════════════════════════════════════════════════════════
// SynthtaxClaimTypes  —  custom JWT claim-typer
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kanoniska claim-typer för Synthtax-specifik JWT-payload.
/// Prefixet "synthtax:" förhindrar kollision med standard-claims.
/// </summary>
public static class SynthtaxClaimTypes
{
    /// <summary>Organisationens Guid-ID. Värde: Guid.ToString("D").</summary>
    public const string OrganizationId   = "synthtax:org_id";

    /// <summary>Organisationens slug, t.ex. "acme-corp". Används för routing.</summary>
    public const string OrganizationSlug = "synthtax:org_slug";

    /// <summary>Användarens roll INOM organisationen. Värde: "Member" eller "OrgAdmin".</summary>
    public const string OrgRole          = "synthtax:org_role";

    /// <summary>Prenumerationsplan. Värde: "Free", "Starter", "Professional", "Enterprise".</summary>
    public const string SubscriptionPlan = "synthtax:plan";

    /// <summary>
    /// Flagga om tokenen avser en system-admin (global) som kan se all data.
    /// Värde: "true" eller saknas.
    /// </summary>
    public const string IsSystemAdmin    = "synthtax:sys_admin";
}

// ═══════════════════════════════════════════════════════════════════════════
// OrgClaimsExtensions  —  hjälpmetoder på ClaimsPrincipal
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Extension-metoder för att läsa SaaS-claims från en <see cref="ClaimsPrincipal"/>.
/// </summary>
public static class OrgClaimsExtensions
{
    /// <summary>Hämtar OrganizationId ur token. Null om saknas eller felformaterat.</summary>
    public static Guid? GetOrganizationId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(SynthtaxClaimTypes.OrganizationId);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static string? GetOrganizationSlug(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(SynthtaxClaimTypes.OrganizationSlug);

    /// <summary>Hämtar OrgRole. Null om saknas eller ej parsningsbar.</summary>
    public static OrgRole? GetOrgRole(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(SynthtaxClaimTypes.OrgRole);
        return Enum.TryParse<OrgRole>(raw, out var role) ? role : null;
    }

    public static SubscriptionPlan? GetSubscriptionPlan(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(SynthtaxClaimTypes.SubscriptionPlan);
        return Enum.TryParse<SubscriptionPlan>(raw, out var plan) ? plan : null;
    }

    public static bool IsSystemAdmin(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(SynthtaxClaimTypes.IsSystemAdmin) == "true"
        || principal.IsInRole("Admin");

    /// <summary>
    /// Returnerar TenantId som ska användas i queries.
    /// = OrganizationId om satt, annars Guid.Empty (system/admin-scope).
    /// </summary>
    public static Guid GetTenantId(this ClaimsPrincipal principal) =>
        principal.GetOrganizationId() ?? Guid.Empty;

    // ── Builder-hjälp för token-generering ───────────────────────────────

    /// <summary>
    /// Skapar claim-listan för en organisationsanvändare.
    /// Anropas av JwtService när en token genereras.
    /// </summary>
    public static IEnumerable<Claim> BuildOrgClaims(
        string           userId,
        string           userName,
        Guid             organizationId,
        string           organizationSlug,
        OrgRole          orgRole,
        SubscriptionPlan plan,
        bool             isSystemAdmin = false)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, userId);
        yield return new Claim(ClaimTypes.Name, userName);
        yield return new Claim(SynthtaxClaimTypes.OrganizationId,   organizationId.ToString("D"));
        yield return new Claim(SynthtaxClaimTypes.OrganizationSlug, organizationSlug);
        yield return new Claim(SynthtaxClaimTypes.OrgRole,          orgRole.ToString());
        yield return new Claim(SynthtaxClaimTypes.SubscriptionPlan, plan.ToString());
        yield return new Claim(ClaimTypes.Role, orgRole.ToString()); // standard-roll för [Authorize(Roles=...)]

        if (isSystemAdmin)
        {
            yield return new Claim(SynthtaxClaimTypes.IsSystemAdmin, "true");
            yield return new Claim(ClaimTypes.Role, "Admin");
        }
    }
}
