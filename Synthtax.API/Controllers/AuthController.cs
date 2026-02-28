using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synthtax.API.Extensions;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : SynthtaxControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtService _jwtService;
    private readonly IUserRepository _userRepository;    // ← interface
    private readonly IAuditLogRepository _auditLog;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtService jwtService,
        IUserRepository userRepository,
        IAuditLogRepository auditLog,
        IOptions<JwtSettings> jwtSettings,
        ILogger<AuthController> logger)
    {
        _userManager    = userManager;
        _signInManager  = signInManager;
        _jwtService     = jwtService;
        _userRepository = userRepository;
        _auditLog       = auditLog;
        _jwtSettings    = jwtSettings.Value;
        _logger         = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = new ApplicationUser
        {
            UserName       = dto.UserName,
            Email          = dto.Email,
            FullName       = dto.FullName,
            EmailConfirmed = true,
            IsActive       = true,
            TenantId       = Guid.Empty,
            CreatedAt      = DateTime.UtcNow,
            Preferences    = new UserPreference
            {
                Theme              = "Light",
                Language           = "sv-SE",
                EmailNotifications = true,
                ShowMetricsTrend   = true,
                DefaultPageSize    = 50
            }
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, "User");

        var response = await BuildAuthResponseAsync(user);

        await _auditLog.LogAsync(user.Id, "Register", "User", user.Id,
            $"New user registered: {user.UserName}", GetClientIp());

        // Pekar på GET /api/users/me istället för på sig själv
        return Created("/api/users/me", response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _userManager.FindByNameAsync(dto.UserName);
        if (user is null || !user.IsActive)
        {
            await _auditLog.LogAsync("anonymous", "Login", "User", null,
                $"Failed login attempt: {dto.UserName}", GetClientIp(), success: false);
            return Unauthorized(new { Message = "Invalid username or password." });
        }

        var result = await _signInManager.CheckPasswordSignInAsync(
            user, dto.Password, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            await _auditLog.LogAsync(user.Id, "Login", "User", user.Id,
                result.IsLockedOut ? "Account locked out" : "Invalid password",
                GetClientIp(), success: false);

            return result.IsLockedOut
                ? Unauthorized(new { Message = "Account is locked. Try again later." })
                : Unauthorized(new { Message = "Invalid username or password." });
        }

        await _userRepository.UpdateLastLoginAsync(user.Id);
        var response = await BuildAuthResponseAsync(user);

        await _auditLog.LogAsync(user.Id, "Login", "User", user.Id,
            $"Successful login: {user.UserName}", GetClientIp());

        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
    {
        var storedToken = await _userRepository.GetRefreshTokenAsync(dto.RefreshToken);
        if (storedToken is null || !storedToken.IsActive)
            return Unauthorized(new { Message = "Invalid or expired refresh token." });

        var user = storedToken.User;
        if (!user.IsActive)
            return Unauthorized(new { Message = "User account is inactive." });

        var newRefreshToken = CreateRefreshToken(user.Id);
        await _userRepository.RevokeRefreshTokenAsync(storedToken, newRefreshToken.Token, GetClientIp());
        await _userRepository.AddRefreshTokenAsync(newRefreshToken);

        var claims      = await BuildClaimsAsync(user);
        var accessToken = _jwtService.GenerateAccessToken(claims);
        var userDto     = await _userRepository.GetUserDtoByIdAsync(user.Id)
                          ?? throw new InvalidOperationException("User not found.");

        return Ok(new AuthResponseDto
        {
            AccessToken  = accessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt    = _jwtService.GetAccessTokenExpiry(),
            User         = userDto
        });
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
    {
        var storedToken = await _userRepository.GetRefreshTokenAsync(dto.RefreshToken);
        if (storedToken is not null && storedToken.IsActive)
            await _userRepository.RevokeRefreshTokenAsync(storedToken, revokedByIp: GetClientIp());

        await _auditLog.LogAsync(GetCurrentUserId(), "Logout", "User", GetCurrentUserId(),
            null, GetClientIp());

        return Ok(new { Message = "Logged out successfully." });
    }

    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = GetCurrentUserId();
        await _userRepository.RevokeAllRefreshTokensForUserAsync(userId);
        await _auditLog.LogAsync(userId, "LogoutAll", "User", userId,
            "Revoked all sessions", GetClientIp());
        return Ok(new { Message = "All sessions revoked." });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<AuthResponseDto> BuildAuthResponseAsync(ApplicationUser user)
    {
        var claims        = await BuildClaimsAsync(user);
        var accessToken   = _jwtService.GenerateAccessToken(claims);
        var refreshToken  = CreateRefreshToken(user.Id);
        await _userRepository.AddRefreshTokenAsync(refreshToken);

        var userDto = await _userRepository.GetUserDtoByIdAsync(user.Id)
                      ?? throw new InvalidOperationException("User not found.");

        return new AuthResponseDto
        {
            AccessToken  = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt    = _jwtService.GetAccessTokenExpiry(),
            User         = userDto
        };
    }

    private async Task<IEnumerable<Claim>> BuildClaimsAsync(ApplicationUser user)
    {
        var roles  = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name,  user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email    ?? string.Empty),
            new("tenant_id",      user.TenantId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        if (!string.IsNullOrEmpty(user.FullName))
            claims.Add(new Claim("full_name", user.FullName));

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        return claims;
    }

    private RefreshToken CreateRefreshToken(string userId) => new()
    {
        Id          = Guid.NewGuid(),
        Token       = _jwtService.GenerateRefreshToken(),
        ExpiresAt   = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
        CreatedAt   = DateTime.UtcNow,
        CreatedByIp = GetClientIp(),
        UserId      = userId
    };
}
