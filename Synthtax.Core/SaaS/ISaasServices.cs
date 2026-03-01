

namespace Synthtax.Core.SaaS;

// ═══════════════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════════════

public sealed record CreateOrganizationRequest(
    string           Name,
    string           Slug,
    string           AdminUserId,
    SubscriptionPlan Plan = SubscriptionPlan.Free,
    string?          BillingEmail = null,
    bool             StartTrial = false);

public sealed record InviteUserRequest(
    Guid    OrganizationId,
    string  Email,
    OrgRole TargetRole,
    string  InvitedByUserId);

public sealed record AcceptInvitationRequest(
    string Token,
    string AcceptingUserId);

public sealed record ServiceResult<T>
{
    public bool    Success { get; init; }
    public T?      Data    { get; init; }
    public string? Error   { get; init; }

    public static ServiceResult<T> Ok(T data) =>
        new() { Success = true, Data = data };

    public static ServiceResult<T> Fail(string error) =>
        new() { Success = false, Error = error };
}

// ═══════════════════════════════════════════════════════════════════════════
// IOrganizationService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hanterar organisationslivscykeln: skapa, byta plan, hantera membres.
/// </summary>
public interface IOrganizationService
{
    Task<ServiceResult<Organization>> CreateAsync(
        CreateOrganizationRequest request, CancellationToken ct = default);

    Task<ServiceResult<Organization>> GetByIdAsync(
        Guid organizationId, CancellationToken ct = default);

    Task<ServiceResult<OrganizationMembership>> DeactivateMemberAsync(
        Guid organizationId, string userId, string deactivatedByUserId,
        CancellationToken ct = default);

    Task<ServiceResult<OrganizationMembership>> ChangeRoleAsync(
        Guid organizationId, string userId, OrgRole newRole,
        string changedByUserId, CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════════════════
// IInvitationService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hanterar inbjudningsflödet: bjud in → acceptera → gå med i organisation.
///
/// <para><b>Säkerhetskrav:</b>
/// <list type="bullet">
///   <item>Tokens är kryptografiskt slumpmässiga (256 bitar, hex).</item>
///   <item>Tokens löper ut efter 7 dagar.</item>
///   <item>En e-post kan bara ha en aktiv inbjudan per organisation.</item>
/// </list>
/// </para>
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// OrgAdmin bjuder in en användare.
    /// Kontrollerar LicenseGuard innan inbjudan skapas.
    /// </summary>
    Task<ServiceResult<Invitation>> InviteAsync(
        InviteUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Accepterar en inbjudan och skapar OrganizationMembership.
    /// Returnerar den skapade membership-posten.
    /// </summary>
    Task<ServiceResult<OrganizationMembership>> AcceptAsync(
        AcceptInvitationRequest request, CancellationToken ct = default);

    /// <summary>OrgAdmin drar tillbaka en inbjudan.</summary>
    Task<ServiceResult<bool>> RevokeAsync(
        Guid invitationId, string revokedByUserId, CancellationToken ct = default);

    /// <summary>Hämtar alla öppna inbjudningar för en organisation.</summary>
    Task<IReadOnlyList<Invitation>> GetPendingAsync(
        Guid organizationId, CancellationToken ct = default);
}
