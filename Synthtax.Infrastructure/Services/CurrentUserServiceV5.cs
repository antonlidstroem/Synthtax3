using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Synthtax.API.SaaS.JWT;
using Synthtax.Domain.Enums;

namespace Synthtax.Infrastructure.Services;

// ═══════════════════════════════════════════════════════════════════════════
// ICurrentUserService  —  Fas 5-version
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kontraktet för aktuell autentiserad användare.
/// Utökar Fas 1-versionen med SaaS-kontext (org, roll, tenant).
///
/// <para><b>ERSÄTTER</b> <c>ICurrentUserService</c> i
/// <c>Synthtax.Infrastructure.Data.Interceptors</c> — ta bort den gamla definitionen
/// och importera denna istället via <c>Synthtax.Infrastructure.Services</c>.</para>
/// </summary>
public interface ICurrentUserService
{
    // ── Identitet ──────────────────────────────────────────────────────────

    /// <summary>ASP.NET Identity UserId. Null för bakgrundsjobb/system.</summary>
    string? UserId { get; }

    // ── Organisations-kontext ──────────────────────────────────────────────

    /// <summary>
    /// Aktiv organisations-ID hämtad ur JWT-claim <c>synthtax:org_id</c>.
    /// Null för anonyma anrop eller global systemadmin utan org-kontext.
    /// </summary>
    Guid? OrganizationId { get; }

    /// <summary>
    /// Tenant-ID för EF Core Global Query Filters.
    /// = <see cref="OrganizationId"/> om satt, annars <c>Guid.Empty</c>.
    /// <c>Guid.Empty</c> används av systemadmin för cross-tenant queries.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>Organisationsroll ur JWT. Null om ej inloggad eller ej i org.</summary>
    OrgRole? OrgRole { get; }

    /// <summary>
    /// True om användaren är global systemadmin (har rollen "Admin" eller
    /// claim <c>synthtax:sys_admin = true</c>).
    /// Systemadmins kringgår tenant-filter i DbContext.
    /// </summary>
    bool IsSystemAdmin { get; }

    bool IsOrgAdmin => OrgRole == Domain.Enums.OrgRole.OrgAdmin || IsSystemAdmin;
}

// ═══════════════════════════════════════════════════════════════════════════
// HttpContextCurrentUserServiceV5  —  HTTP-implementation
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Läser alla SaaS-claims från JWT via IHttpContextAccessor.
/// Registreras som Scoped (en instans per HTTP-request).
/// </summary>
public sealed class HttpContextCurrentUserServiceV5 : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    // Lazy-beräknade properties för att undvika upprepade claim-lookups
    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public HttpContextCurrentUserServiceV5(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public Guid? OrganizationId =>
        Principal?.GetOrganizationId();

    public Guid TenantId =>
        Principal?.GetTenantId() ?? Guid.Empty;

    public OrgRole? OrgRole =>
        Principal?.GetOrgRole();

    public bool IsSystemAdmin =>
        Principal?.IsSystemAdmin() ?? false;
}

// ═══════════════════════════════════════════════════════════════════════════
// SystemCurrentUserService  —  för bakgrundsjobb och tester
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Systemidentitet utan HTTP-kontext — används av bakgrundsjobb, seed-services
/// och enhetstester som inte vill mocka IHttpContextAccessor.
/// </summary>
public sealed class SystemCurrentUserService : ICurrentUserService
{
    public string? UserId         => null;        // "system" via DbContext-fallback
    public Guid?   OrganizationId => null;
    public Guid    TenantId       => Guid.Empty;  // cross-tenant
    public OrgRole? OrgRole       => null;
    public bool    IsSystemAdmin  => true;         // bakgrundsjobb kringgår tenant-filter
}
