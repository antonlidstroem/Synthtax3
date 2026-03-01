using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.SaaS.Filters;
using Synthtax.API.SaaS.JWT;
using Synthtax.Core.SaaS;
using Synthtax.Domain.Enums;
using Synthtax.Infrastructure.Services;

namespace Synthtax.API.SaaS;

/// <summary>
/// REST API för organisations- och inbjudningshantering.
///
/// <para><b>Behörighetsnivåer:</b>
/// <list type="bullet">
///   <item>GET-endpoints: alla autentiserade membres.</item>
///   <item>POST/PUT/DELETE: kräver OrgAdmin-roll eller SystemAdmin.</item>
///   <item>Bjuda in användare: kontrolleras dessutom av LicenseGuard.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/organizations")]
[Authorize]
public sealed class OrganizationController : ControllerBase
{
    private readonly IOrganizationService  _orgService;
    private readonly IInvitationService    _invitationService;
    private readonly ILicenseGuard         _licenseGuard;
    private readonly ICurrentUserService   _currentUser;

    public OrganizationController(
        IOrganizationService  orgService,
        IInvitationService    invitationService,
        ILicenseGuard         licenseGuard,
        ICurrentUserService   currentUser)
    {
        _orgService        = orgService;
        _invitationService = invitationService;
        _licenseGuard      = licenseGuard;
        _currentUser       = currentUser;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Organisation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Skapar en ny organisation och gör anroparen till OrgAdmin.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrgDto), 201)]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    public async Task<IActionResult> CreateOrganization(
        [FromBody] CreateOrgRequest req,
        CancellationToken ct)
    {
        if (_currentUser.UserId is null)
            return Unauthorized();

        var result = await _orgService.CreateAsync(new CreateOrganizationRequest(
            Name:         req.Name,
            Slug:         req.Slug,
            AdminUserId:  _currentUser.UserId,
            Plan:         SubscriptionPlan.Free,
            BillingEmail: req.BillingEmail,
            StartTrial:   true), ct);

        if (!result.Success)
            return BadRequest(new ErrorDto(result.Error!));

        return CreatedAtAction(nameof(GetOrganization),
            new { id = result.Data!.Id },
            OrgDto.From(result.Data));
    }

    /// <summary>Hämtar aktuell organisations info.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrgDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOrganization(Guid id, CancellationToken ct)
    {
        // Tenant-filtret i DbContext säkerställer att man inte kan hämta andras org
        var result = await _orgService.GetByIdAsync(id, ct);

        return result.Success
            ? Ok(OrgDto.From(result.Data!))
            : NotFound();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Membres
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// OrgAdmin inaktiverar en member.
    /// Förhindrar inaktivering av sista OrgAdmin.
    /// </summary>
    [HttpDelete("members/{userId}")]
    [Authorize(Roles = "OrgAdmin,Admin")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> DeactivateMember(
        string userId, CancellationToken ct)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Forbid();

        var result = await _orgService.DeactivateMemberAsync(
            orgId.Value, userId, _currentUser.UserId!, ct);

        return result.Success
            ? NoContent()
            : BadRequest(new ErrorDto(result.Error!));
    }

    /// <summary>OrgAdmin ändrar en members roll.</summary>
    [HttpPut("members/{userId}/role")]
    [Authorize(Roles = "OrgAdmin,Admin")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    public async Task<IActionResult> ChangeRole(
        string userId,
        [FromBody] ChangeRoleRequest req,
        CancellationToken ct)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Forbid();

        var result = await _orgService.ChangeRoleAsync(
            orgId.Value, userId, req.NewRole, _currentUser.UserId!, ct);

        return result.Success
            ? NoContent()
            : BadRequest(new ErrorDto(result.Error!));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Inbjudningar
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// OrgAdmin bjuder in en användare via e-post.
    /// LicenseGuard kontrollerar att licenskvotan inte överskrids.
    /// </summary>
    [HttpPost("invitations")]
    [Authorize(Roles = "OrgAdmin,Admin")]
    [ProducesResponseType(typeof(InvitationDto), 201)]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    [ProducesResponseType(typeof(LicenseDeniedDto), 402)]
    public async Task<IActionResult> InviteUser(
        [FromBody] InviteUserRequestDto req,
        CancellationToken ct)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Forbid();

        // Explicit licenscheck — ger tydligt 402-svar om kvoten nåtts
        var licCheck = await _licenseGuard.CheckUserInviteAllowedAsync(orgId.Value, ct);
        if (!licCheck.IsAllowed)
            return StatusCode(402, new LicenseDeniedDto(
                licCheck.DenialReason!,
                licCheck.UpgradeHint,
                licCheck.CurrentCount,
                licCheck.LimitCount));

        var result = await _invitationService.InviteAsync(new InviteUserRequest(
            OrganizationId: orgId.Value,
            Email:          req.Email,
            TargetRole:     req.Role,
            InvitedByUserId: _currentUser.UserId!), ct);

        return result.Success
            ? StatusCode(201, InvitationDto.From(result.Data!))
            : BadRequest(new ErrorDto(result.Error!));
    }

    /// <summary>Hämtar alla öppna inbjudningar för organisationen.</summary>
    [HttpGet("invitations")]
    [Authorize(Roles = "OrgAdmin,Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<InvitationDto>), 200)]
    public async Task<IActionResult> GetPendingInvitations(CancellationToken ct)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Forbid();

        var invitations = await _invitationService.GetPendingAsync(orgId.Value, ct);
        return Ok(invitations.Select(InvitationDto.From).ToList());
    }

    /// <summary>
    /// Återkallar en inbjudan.
    /// Kräver OrgAdmin-behörighet.
    /// </summary>
    [HttpDelete("invitations/{id:guid}")]
    [Authorize(Roles = "OrgAdmin,Admin")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    public async Task<IActionResult> RevokeInvitation(Guid id, CancellationToken ct)
    {
        var result = await _invitationService.RevokeAsync(id, _currentUser.UserId!, ct);
        return result.Success ? NoContent() : BadRequest(new ErrorDto(result.Error!));
    }

    /// <summary>
    /// Accepterar en inbjudan via token.
    /// Anropas av ny användare efter klick i inbjudningsmail.
    /// </summary>
    [HttpPost("invitations/accept")]
    [AllowAnonymous]  // användaren är inte inloggad i org ännu
    [ProducesResponseType(typeof(MembershipDto), 200)]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    public async Task<IActionResult> AcceptInvitation(
        [FromBody] AcceptInvitationDto req,
        CancellationToken ct)
    {
        if (_currentUser.UserId is null)
            return Unauthorized(new ErrorDto("Du måste vara inloggad för att acceptera en inbjudan."));

        var result = await _invitationService.AcceptAsync(new AcceptInvitationRequest(
            Token:            req.Token,
            AcceptingUserId:  _currentUser.UserId), ct);

        return result.Success
            ? Ok(MembershipDto.From(result.Data!))
            : BadRequest(new ErrorDto(result.Error!));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Licenskontroll — diagnostik-endpoint
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returnerar aktuella licens-gränsvärden och användning för org.
    /// Används av frontend för att visa uppgraderingsprompts.
    /// </summary>
    [HttpGet("license-status")]
    [ProducesResponseType(typeof(LicenseStatusDto), 200)]
    public async Task<IActionResult> GetLicenseStatus(CancellationToken ct)
    {
        var orgId = _currentUser.OrganizationId;
        if (orgId is null) return Forbid();

        var scanCheck    = await _licenseGuard.CheckScanAllowedAsync(orgId.Value, ct);
        var inviteCheck  = await _licenseGuard.CheckUserInviteAllowedAsync(orgId.Value, ct);
        var fuzzyCheck   = await _licenseGuard.CheckFeatureAllowedAsync(
            orgId.Value, LicenseFeature.FuzzyMatching, ct);
        var ciCheck      = await _licenseGuard.CheckFeatureAllowedAsync(
            orgId.Value, LicenseFeature.CiCdIntegration, ct);

        return Ok(new LicenseStatusDto(
            ScanQuota:           new QuotaDto(scanCheck.CurrentCount, scanCheck.LimitCount, scanCheck.IsAllowed),
            MemberQuota:         new QuotaDto(inviteCheck.CurrentCount, inviteCheck.LimitCount, inviteCheck.IsAllowed),
            FuzzyMatchingEnabled: fuzzyCheck.IsAllowed,
            CiCdEnabled:          ciCheck.IsAllowed,
            UpgradeHint:          scanCheck.UpgradeHint ?? inviteCheck.UpgradeHint));
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Request/Response DTOs (lokala — API-lagret)
// ═══════════════════════════════════════════════════════════════════════════

public sealed record CreateOrgRequest(string Name, string Slug, string? BillingEmail);
public sealed record ChangeRoleRequest(OrgRole NewRole);
public sealed record InviteUserRequestDto(string Email, OrgRole Role = OrgRole.Member);
public sealed record AcceptInvitationDto(string Token);

public sealed record ErrorDto(string Error);

public sealed record LicenseDeniedDto(
    string Reason,
    string? UpgradeHint,
    int CurrentCount,
    int LimitCount);

public sealed record OrgDto(
    Guid             Id,
    string           Name,
    string           Slug,
    SubscriptionPlan Plan,
    int              ActiveMemberCount,
    bool             IsInTrial,
    DateTime?        TrialEndsAt)
{
    public static OrgDto From(Domain.Entities.Organization o) => new(
        o.Id,
        o.Name,
        o.Slug,
        o.Plan,
        o.ActiveMemberCount,
        o.IsInTrial,
        o.TrialEndsAt);
}

public sealed record InvitationDto(
    Guid                         Id,
    string                       Email,
    OrgRole                      TargetRole,
    Domain.Enums.InvitationStatus Status,
    DateTime                     ExpiresAt)
{
    public static InvitationDto From(Domain.Entities.Invitation i) => new(
        i.Id, i.Email, i.TargetRole, i.Status, i.ExpiresAt);
}

public sealed record MembershipDto(
    Guid    Id,
    string  UserId,
    OrgRole Role,
    bool    IsActive)
{
    public static MembershipDto From(Domain.Entities.OrganizationMembership m) => new(
        m.Id, m.UserId, m.Role, m.IsActive);
}

public sealed record LicenseStatusDto(
    QuotaDto ScanQuota,
    QuotaDto MemberQuota,
    bool     FuzzyMatchingEnabled,
    bool     CiCdEnabled,
    string?  UpgradeHint);

public sealed record QuotaDto(int Current, int Limit, bool HasCapacity);
