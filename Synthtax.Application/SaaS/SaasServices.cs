using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Core.SaaS;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;
using Synthtax.Infrastructure.Data;
using System.Security.Cryptography;

namespace Synthtax.Application.SaaS;

// ═══════════════════════════════════════════════════════════════════════════
// OrganizationService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Hanterar organisationslivscykeln.</summary>
public sealed class OrganizationService : IOrganizationService
{
    private readonly SynthtaxDbContextV5          _db;
    private readonly ILicenseGuard                _guard;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        SynthtaxDbContextV5          db,
        ILicenseGuard                guard,
        ILogger<OrganizationService> logger)
    {
        _db     = db;
        _guard  = guard;
        _logger = logger;
    }

    public async Task<ServiceResult<Organization>> CreateAsync(
        CreateOrganizationRequest request, CancellationToken ct = default)
    {
        // ── Valideringar ──────────────────────────────────────────────────

        if (string.IsNullOrWhiteSpace(request.Name))
            return ServiceResult<Organization>.Fail("Organisationsnamnet får inte vara tomt.");

        if (string.IsNullOrWhiteSpace(request.Slug))
            return ServiceResult<Organization>.Fail("Slug får inte vara tom.");

        var slugNormalized = NormalizeSlug(request.Slug);
        if (!IsValidSlug(slugNormalized))
            return ServiceResult<Organization>.Fail(
                "Slug får bara innehålla gemener, siffror och bindestreck (a-z, 0-9, -).");

        // Kontrollera unik slug (IgnoreQueryFilters — cross-tenant)
        var slugExists = await _db.Organizations
            .IgnoreQueryFilters()
            .AnyAsync(o => o.Slug == slugNormalized, ct);

        if (slugExists)
            return ServiceResult<Organization>.Fail($"Slug '{slugNormalized}' är redan tagen.");

        // ── Skapa organisation + admin-membership ─────────────────────────

        var org = new Organization
        {
            Id               = Guid.NewGuid(),
            Name             = request.Name.Trim(),
            Slug             = slugNormalized,
            Plan             = request.Plan,
            PurchasedLicenses = 1,
            BillingEmail     = request.BillingEmail,
            IsActive         = true,
            TrialEndsAt      = request.StartTrial ? DateTime.UtcNow.AddDays(14) : null
        };

        var adminMembership = new OrganizationMembership
        {
            Id             = Guid.NewGuid(),
            OrganizationId = org.Id,
            UserId         = request.AdminUserId,
            Role           = OrgRole.OrgAdmin,
            IsActive       = true,
            JoinedAt       = DateTime.UtcNow
        };

        _db.Organizations.Add(org);
        _db.OrganizationMemberships.Add(adminMembership);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Organisation skapad: {OrgId} ({Slug}) av {AdminId}",
            org.Id, org.Slug, request.AdminUserId);

        return ServiceResult<Organization>.Ok(org);
    }

    public async Task<ServiceResult<Organization>> GetByIdAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        var org = await _db.Organizations
            .Include(o => o.Memberships)
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);

        return org is null
            ? ServiceResult<Organization>.Fail("Organisation hittades inte.")
            : ServiceResult<Organization>.Ok(org);
    }

    public async Task<ServiceResult<OrganizationMembership>> DeactivateMemberAsync(
        Guid organizationId, string userId, string deactivatedByUserId,
        CancellationToken ct = default)
    {
        var membership = await _db.OrganizationMemberships
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.IsActive, ct);

        if (membership is null)
            return ServiceResult<OrganizationMembership>.Fail("Aktiv membership hittades inte.");

        // Förhindra att sista OrgAdmin inaktiveras
        if (membership.Role == OrgRole.OrgAdmin)
        {
            var adminCount = await _db.OrganizationMemberships
                .CountAsync(m =>
                    m.OrganizationId == organizationId &&
                    m.Role == OrgRole.OrgAdmin &&
                    m.IsActive &&
                    m.UserId != userId, ct);

            if (adminCount == 0)
                return ServiceResult<OrganizationMembership>.Fail(
                    "Kan inte inaktivera sista OrgAdmin. Utse en annan admin först.");
        }

        membership.IsActive       = false;
        membership.DeactivatedAt  = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Membership inaktiverat: UserId:{UserId} OrgId:{OrgId} av {By}",
            userId, organizationId, deactivatedByUserId);

        return ServiceResult<OrganizationMembership>.Ok(membership);
    }

    public async Task<ServiceResult<OrganizationMembership>> ChangeRoleAsync(
        Guid organizationId, string userId, OrgRole newRole,
        string changedByUserId, CancellationToken ct = default)
    {
        var membership = await _db.OrganizationMemberships
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == organizationId &&
                m.UserId == userId &&
                m.IsActive, ct);

        if (membership is null)
            return ServiceResult<OrganizationMembership>.Fail("Aktiv membership hittades inte.");

        membership.Role = newRole;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Rol ändrad: UserId:{UserId} OrgId:{OrgId} → {Role} av {By}",
            userId, organizationId, newRole, changedByUserId);

        return ServiceResult<OrganizationMembership>.Ok(membership);
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private static string NormalizeSlug(string slug) =>
        slug.Trim().ToLowerInvariant().Replace(' ', '-');

    private static bool IsValidSlug(string slug) =>
        !string.IsNullOrEmpty(slug) &&
        slug.Length is >= 3 and <= 100 &&
        System.Text.RegularExpressions.Regex.IsMatch(slug, @"^[a-z0-9][a-z0-9\-]*[a-z0-9]$");
}

// ═══════════════════════════════════════════════════════════════════════════
// InvitationService
// ═══════════════════════════════════════════════════════════════════════════

public sealed class InvitationService : IInvitationService
{
    private readonly SynthtaxDbContextV5         _db;
    private readonly ILicenseGuard               _guard;
    private readonly ILogger<InvitationService>  _logger;

    private const int TokenBytesLength    = 32; // 256 bitar
    private const int InvitationValidDays = 7;

    public InvitationService(
        SynthtaxDbContextV5        db,
        ILicenseGuard              guard,
        ILogger<InvitationService> logger)
    {
        _db     = db;
        _guard  = guard;
        _logger = logger;
    }

    public async Task<ServiceResult<Invitation>> InviteAsync(
        InviteUserRequest request, CancellationToken ct = default)
    {
        // ── 1. Licenscheck ─────────────────────────────────────────────────
        var licenseCheck = await _guard.CheckUserInviteAllowedAsync(request.OrganizationId, ct);
        if (!licenseCheck.IsAllowed)
            return ServiceResult<Invitation>.Fail(
                licenseCheck.DenialReason + " " + licenseCheck.UpgradeHint);

        // ── 2. Validera e-postformat ──────────────────────────────────────
        if (!IsValidEmail(request.Email))
            return ServiceResult<Invitation>.Fail($"Ogiltig e-postadress: {request.Email}");

        // ── 3. Kontrollera att en aktiv inbjudan inte redan finns ─────────
        var existingPending = await _db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i =>
                i.OrganizationId == request.OrganizationId &&
                i.Email == request.Email.ToLowerInvariant() &&
                i.Status == InvitationStatus.Pending, ct);

        if (existingPending is not null)
        {
            if (!existingPending.IsExpired)
                return ServiceResult<Invitation>.Fail(
                    $"En aktiv inbjudan till {request.Email} finns redan (löper ut {existingPending.ExpiresAt:d}).");

            // Gammal utgången inbjudan — ogiltigförklara och skapa ny
            existingPending.Status = InvitationStatus.Expired;
        }

        // ── 4. Kontrollera att användaren inte redan är aktiv member ──────
        var alreadyMember = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .AnyAsync(m =>
                m.OrganizationId == request.OrganizationId &&
                m.IsActive, ct);
        // (Förenkling: en mer fullständig implementation kollar mot user's e-post)

        // ── 5. Skapa inbjudan ─────────────────────────────────────────────
        var token      = GenerateSecureToken();
        var invitation = new Invitation
        {
            Id             = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Email          = request.Email.ToLowerInvariant(),
            TargetRole     = request.TargetRole,
            Token          = token,
            Status         = InvitationStatus.Pending,
            ExpiresAt      = DateTime.UtcNow.AddDays(InvitationValidDays)
        };

        _db.Invitations.Add(invitation);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Inbjudan skapad: OrgId:{OrgId} Email:{Email} Role:{Role} av {By}",
            request.OrganizationId, request.Email, request.TargetRole, request.InvitedByUserId);

        return ServiceResult<Invitation>.Ok(invitation);
    }

    public async Task<ServiceResult<OrganizationMembership>> AcceptAsync(
        AcceptInvitationRequest request, CancellationToken ct = default)
    {
        // ── 1. Hitta och validera inbjudan ────────────────────────────────
        var invitation = await _db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.Token == request.Token, ct);

        if (invitation is null)
            return ServiceResult<OrganizationMembership>.Fail("Ogiltig inbjudningslänk.");

        if (!invitation.CanBeAccepted)
        {
            var reason = invitation.Status switch
            {
                InvitationStatus.Accepted => "Inbjudan är redan accepterad.",
                InvitationStatus.Revoked  => "Inbjudan har återkallats.",
                InvitationStatus.Expired  => "Inbjudan har löpt ut.",
                _                         => invitation.IsExpired ? "Inbjudan har löpt ut." : "Inbjudan är inte giltig."
            };
            return ServiceResult<OrganizationMembership>.Fail(reason);
        }

        // ── 2. Kontrollera att användaren inte redan är member ────────────
        var existingMembership = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m =>
                m.OrganizationId == invitation.OrganizationId &&
                m.UserId == request.AcceptingUserId, ct);

        if (existingMembership?.IsActive == true)
            return ServiceResult<OrganizationMembership>.Fail(
                "Du är redan aktiv member i denna organisation.");

        // ── 3. Skapa membership + markera inbjudan accepterad ─────────────
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var membership = existingMembership ?? new OrganizationMembership
            {
                Id             = Guid.NewGuid(),
                OrganizationId = invitation.OrganizationId,
                UserId         = request.AcceptingUserId
            };

            membership.Role      = invitation.TargetRole;
            membership.IsActive  = true;
            membership.JoinedAt  = DateTime.UtcNow;

            if (existingMembership is null)
                _db.OrganizationMemberships.Add(membership);

            invitation.Status           = InvitationStatus.Accepted;
            invitation.AcceptedAt       = DateTime.UtcNow;
            invitation.AcceptedByUserId = request.AcceptingUserId;

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            _logger.LogInformation(
                "Inbjudan accepterad: OrgId:{OrgId} UserId:{UserId} Role:{Role}",
                invitation.OrganizationId, request.AcceptingUserId, invitation.TargetRole);

            return ServiceResult<OrganizationMembership>.Ok(membership);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<ServiceResult<bool>> RevokeAsync(
        Guid invitationId, string revokedByUserId, CancellationToken ct = default)
    {
        var invitation = await _db.Invitations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i =>
                i.Id == invitationId &&
                i.Status == InvitationStatus.Pending, ct);

        if (invitation is null)
            return ServiceResult<bool>.Fail("Aktiv inbjudan hittades inte.");

        invitation.Status = InvitationStatus.Revoked;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Inbjudan återkallad: Id:{Id} av {By}",
            invitationId, revokedByUserId);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<IReadOnlyList<Invitation>> GetPendingAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        return await _db.Invitations
            .Where(i =>
                i.OrganizationId == organizationId &&
                i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(ct);
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytesLength);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) &&
        System.Text.RegularExpressions.Regex.IsMatch(email,
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
