using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synthtax.API.Extensions;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Repositories;
using System.IdentityModel.Tokens.Jwt;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtService _jwtService;
    private readonly UserRepository _userRepository;
    private readonly IAuditLogRepository _auditLog;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtService jwtService,
        UserRepository userRepository,
        IAuditLogRepository auditLog,
        IOptions<JwtSettings> jwtSettings,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _userRepository = userRepository;
        _auditLog = auditLog;
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
    }

    /// <summary>Registrerar en ny användare (kräver Admin-roll om systemet redan har användare).</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = new ApplicationUser
        {
            UserName = dto.UserName,
            Email = dto.Email,
            FullName = dto.FullName,
            EmailConfirmed = true,
            IsActive = true,
            TenantId = Guid.Empty,
            CreatedAt = DateTime.UtcNow,
            Preferences = new UserPreference
            {
                Theme = "Light",
                Language = "sv-SE",
                EmailNotifications = true,
                ShowMetricsTrend = true,
                DefaultPageSize = 50
            }
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

        await _userManager.AddToRoleAsync(user, "User");

        var response = await BuildAuthResponse(user);
        await _auditLog.LogAsync(user.Id, "Register", "User", user.Id,
            $"New user registered: {user.UserName}", GetClientIp());

        return CreatedAtAction(nameof(Register), response);
    }

    /// <summary>Loggar in och returnerar JWT + refresh token.</summary>
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

        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            await _auditLog.LogAsync(user.Id, "Login", "User", user.Id,
                result.IsLockedOut ? "Account locked out" : "Invalid password",
                GetClientIp(), success: false);

            if (result.IsLockedOut)
                return Unauthorized(new { Message = "Account is locked. Try again later." });

            return Unauthorized(new { Message = "Invalid username or password." });
        }

        await _userRepository.UpdateLastLoginAsync(user.Id);
        var response = await BuildAuthResponse(user);

        await _auditLog.LogAsync(user.Id, "Login", "User", user.Id,
            $"Successful login: {user.UserName}", GetClientIp());

        return Ok(response);
    }

    /// <summary>Förnyar access token med ett giltigt refresh token.</summary>
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

        // Rulla refresh token
        var newRefreshToken = CreateRefreshToken(user.Id);
        await _userRepository.RevokeRefreshTokenAsync(storedToken, newRefreshToken.Token, GetClientIp());
        await _userRepository.AddRefreshTokenAsync(newRefreshToken);

        var claims = await BuildClaimsAsync(user);
        var accessToken = _jwtService.GenerateAccessToken(claims);

        var userDto = await _userRepository.GetUserDtoByIdAsync(user.Id)
                      ?? throw new InvalidOperationException("User not found.");

        return Ok(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = _jwtService.GetAccessTokenExpiry(),
            User = userDto
        });
    }

    /// <summary>Loggar ut och återkallar refresh token.</summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
    {
        var storedToken = await _userRepository.GetRefreshTokenAsync(dto.RefreshToken);
        if (storedToken is not null && storedToken.IsActive)
            await _userRepository.RevokeRefreshTokenAsync(storedToken, revokedByIp: GetClientIp());

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        await _auditLog.LogAsync(userId, "Logout", "User", userId, null, GetClientIp());

        return Ok(new { Message = "Logged out successfully." });
    }

    /// <summary>Loggar ut från alla enheter (återkallar alla refresh tokens).</summary>
    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        await _userRepository.RevokeAllRefreshTokensForUserAsync(userId);
        await _auditLog.LogAsync(userId, "LogoutAll", "User", userId,
            "Revoked all sessions", GetClientIp());
        return Ok(new { Message = "All sessions revoked." });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<AuthResponseDto> BuildAuthResponse(ApplicationUser user)
    {
        var claims = await BuildClaimsAsync(user);
        var accessToken = _jwtService.GenerateAccessToken(claims);
        var refreshToken = CreateRefreshToken(user.Id);

        await _userRepository.AddRefreshTokenAsync(refreshToken);

        var userDto = await _userRepository.GetUserDtoByIdAsync(user.Id)
                      ?? throw new InvalidOperationException("User not found.");

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = _jwtService.GetAccessTokenExpiry(),
            User = userDto
        };
    }

    private async Task<IEnumerable<Claim>> BuildClaimsAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("tenant_id", user.TenantId.ToString()),
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
        Id = Guid.NewGuid(),
        Token = _jwtService.GenerateRefreshToken(),
        ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
        CreatedAt = DateTime.UtcNow,
        CreatedByIp = GetClientIp(),
        UserId = userId
    };

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}
