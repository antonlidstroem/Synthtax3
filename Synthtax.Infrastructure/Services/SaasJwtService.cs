using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Synthtax.API.SaaS.JWT;
using Synthtax.Domain.Enums;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Services;

// ═══════════════════════════════════════════════════════════════════════════
// ISaasJwtService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Genererar JWT-tokens med org-kontext inbakad.
/// Ersätter/kompletterar befintlig <c>IJwtService</c>.
/// </summary>
public interface ISaasJwtService
{
    /// <summary>
    /// Skapar en token för en användare i en specifik organisation.
    /// Bär claims: UserId, UserName, OrgId, OrgSlug, OrgRole, SubscriptionPlan.
    /// </summary>
    Task<TokenPair> GenerateForUserInOrgAsync(
        string userId,
        Guid   organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Validerar en access-token och returnerar dess ClaimsPrincipal.
    /// Null om token är ogiltig eller utgången.
    /// </summary>
    ClaimsPrincipal? Validate(string accessToken);

    /// <summary>Förnyar ett token-par via refresh-token.</summary>
    Task<TokenPair?> RefreshAsync(string refreshToken, CancellationToken ct = default);
}

/// <summary>Access + refresh-tokenpar.</summary>
public sealed record TokenPair(
    string   AccessToken,
    string   RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt);

// ═══════════════════════════════════════════════════════════════════════════
// SaasJwtService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// JWT-implementation med SaaS-claims.
///
/// <para><b>Token-innehåll (payload):</b>
/// <code>
/// {
///   "sub":                "user-guid",
///   "name":               "alice",
///   "synthtax:org_id":    "org-guid",
///   "synthtax:org_slug":  "acme-corp",
///   "synthtax:org_role":  "OrgAdmin",
///   "synthtax:plan":      "Professional",
///   "role":               "OrgAdmin",
///   "exp":                1234567890
/// }
/// </code>
/// </para>
/// </summary>
public sealed class SaasJwtService : ISaasJwtService
{
    private readonly SynthtaxDbContextV5        _db;
    private readonly IConfiguration             _config;
    private readonly ILogger<SaasJwtService>    _logger;

    // Konfigurationsnycklar — matchar appsettings.json
    private const string SecretKeyPath    = "Jwt:SecretKey";
    private const string IssuerPath       = "Jwt:Issuer";
    private const string AudiencePath     = "Jwt:Audience";
    private const string AccessExpiryPath = "Jwt:AccessTokenExpiryMinutes";
    private const string RefreshExpiryPath = "Jwt:RefreshTokenExpiryDays";

    public SaasJwtService(
        SynthtaxDbContextV5     db,
        IConfiguration          config,
        ILogger<SaasJwtService> logger)
    {
        _db     = db;
        _config = config;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ISaasJwtService
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<TokenPair> GenerateForUserInOrgAsync(
        string userId,
        Guid   organizationId,
        CancellationToken ct = default)
    {
        // ── 1. Hämta user + membership + org i en query ────────────────────
        var context = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(m =>
                m.UserId         == userId         &&
                m.OrganizationId == organizationId &&
                m.IsActive)
            .Select(m => new
            {
                UserId           = m.UserId,
                OrgId            = m.OrganizationId,
                OrgRole          = m.Role,
                OrgSlug          = m.Organization.Slug,
                Plan             = m.Organization.Plan,
                OrgIsActive      = m.Organization.IsActive,
                UserName         = (string?)null  // hämtas separat via Identity
            })
            .FirstOrDefaultAsync(ct);

        if (context is null)
            throw new InvalidOperationException(
                $"Aktiv membership saknas för user={userId} org={organizationId}.");

        if (!context.OrgIsActive)
            throw new InvalidOperationException("Organisationen är inaktiverad.");

        // ── 2. Hämta username ur Identity-tabellen (undviker circular dependency) ──
        var userName = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.UserName ?? u.Email ?? userId)
            .FirstOrDefaultAsync(ct) ?? userId;

        // ── 3. Bygg claims ─────────────────────────────────────────────────
        var claims = OrgClaimsExtensions.BuildOrgClaims(
            userId:           userId,
            userName:         userName,
            organizationId:   organizationId,
            organizationSlug: context.OrgSlug,
            orgRole:          context.OrgRole,
            plan:             context.Plan).ToList();

        // ── 4. Signera och serialisera ─────────────────────────────────────
        var accessToken        = CreateAccessToken(claims);
        var (refreshToken, rt) = await CreateRefreshTokenAsync(userId, ct);
        var accessExpiry       = DateTime.UtcNow.AddMinutes(GetAccessExpiryMinutes());
        var refreshExpiry      = rt;

        _logger.LogDebug(
            "JWT genererat för UserId:{UserId} OrgId:{OrgId} Role:{Role}",
            userId, organizationId, context.OrgRole);

        return new TokenPair(accessToken, refreshToken, accessExpiry, refreshExpiry);
    }

    public ClaimsPrincipal? Validate(string accessToken)
    {
        try
        {
            var handler    = new JwtSecurityTokenHandler();
            var parameters = BuildValidationParameters(validateLifetime: true);
            var principal  = handler.ValidateToken(accessToken, parameters, out _);
            return principal;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogDebug("JWT-validering misslyckades: {Msg}", ex.Message);
            return null;
        }
    }

    public async Task<TokenPair?> RefreshAsync(
        string refreshToken, CancellationToken ct = default)
    {
        // Hämta befintlig refresh-token ur DB
        var stored = await _db.Set<RefreshToken>()
            .Include(r => r.User)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r =>
                r.Token     == refreshToken &&
                !r.IsRevoked &&
                !r.IsUsed   &&
                r.ExpiresAt > DateTime.UtcNow, ct);

        if (stored is null)
        {
            _logger.LogWarning("Refresh-token ogiltigt eller utgånget.");
            return null;
        }

        // Hämta org-kontext från det gamla access-tokenet
        var principal = ValidateIgnoreLifetime(stored.LastAccessToken ?? "");
        var orgId     = principal?.GetOrganizationId();

        if (orgId is null)
        {
            _logger.LogWarning("Kunde inte återskapa org-kontext från refresh-token.");
            return null;
        }

        // Markera gammal token som använd
        stored.IsUsed = true;
        await _db.SaveChangesAsync(ct);

        return await GenerateForUserInOrgAsync(stored.UserId, orgId.Value, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privata hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private string CreateAccessToken(IEnumerable<Claim> claims)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(GetSecretKey()));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry      = DateTime.UtcNow.AddMinutes(GetAccessExpiryMinutes());

        var token = new JwtSecurityToken(
            issuer:             _config[IssuerPath],
            audience:           _config[AudiencePath],
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            expiry,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<(string Token, DateTime Expiry)> CreateRefreshTokenAsync(
        string userId, CancellationToken ct)
    {
        var expiryDays = int.TryParse(_config[RefreshExpiryPath], out var d) ? d : 30;
        var expiry     = DateTime.UtcNow.AddDays(expiryDays);
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token      = Convert.ToBase64String(tokenBytes);

        var refreshToken = new RefreshToken
        {
            Id        = Guid.NewGuid(),
            UserId    = userId,
            Token     = token,
            ExpiresAt = expiry,
            IsRevoked = false,
            IsUsed    = false
        };

        _db.Set<RefreshToken>().Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        return (token, expiry);
    }

    private TokenValidationParameters BuildValidationParameters(bool validateLifetime) =>
        new()
        {
            ValidateIssuer           = true,
            ValidIssuer              = _config[IssuerPath],
            ValidateAudience         = true,
            ValidAudience            = _config[AudiencePath],
            ValidateLifetime         = validateLifetime,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(GetSecretKey())),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };

    private ClaimsPrincipal? ValidateIgnoreLifetime(string token)
    {
        try
        {
            return new JwtSecurityTokenHandler().ValidateToken(
                token,
                BuildValidationParameters(validateLifetime: false),
                out _);
        }
        catch { return null; }
    }

    private string GetSecretKey() =>
        _config[SecretKeyPath]
        ?? throw new InvalidOperationException($"Konfiguration saknas: {SecretKeyPath}");

    private int GetAccessExpiryMinutes() =>
        int.TryParse(_config[AccessExpiryPath], out var m) ? m : 60;
}

/// <summary>
/// Minimal stub för RefreshToken-entiteten — matchar befintlig infrastruktur.
/// Flytta/byt ut mot befintlig entitet om en redan finns.
/// </summary>
public class RefreshToken
{
    public Guid     Id              { get; set; }
    public string   UserId          { get; set; } = string.Empty;
    public string   Token           { get; set; } = string.Empty;
    public DateTime ExpiresAt       { get; set; }
    public bool     IsRevoked       { get; set; }
    public bool     IsUsed          { get; set; }
    public string?  LastAccessToken { get; set; }

    // Navigation
    public ApplicationUser User { get; set; } = null!;
}
