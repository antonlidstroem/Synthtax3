using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Repositories;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserRepository _userRepository;
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        UserRepository userRepository,
        IAuditLogRepository auditLog,
        ILogger<UsersController> logger)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <summary>Hämtar den inloggade användarens profil.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMe()
    {
        var userId = GetCurrentUserId();
        var user = await _userRepository.GetUserDtoByIdAsync(userId);
        return user is null ? NotFound() : Ok(user);
    }

    /// <summary>Uppdaterar visningsnamn och e-post.</summary>
    [HttpPut("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        var userId = GetCurrentUserId();
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(dto.FullName))
            user.FullName = dto.FullName;

        if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
        {
            var setEmailResult = await _userManager.SetEmailAsync(user, dto.Email);
            if (!setEmailResult.Succeeded)
                return BadRequest(new { Errors = setEmailResult.Errors.Select(e => e.Description) });
        }

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return BadRequest(new { Errors = updateResult.Errors.Select(e => e.Description) });

        await _auditLog.LogAsync(userId, "UpdateProfile", "User", userId, null, GetClientIp());

        var userDto = await _userRepository.GetUserDtoByIdAsync(userId);
        return Ok(userDto);
    }

    /// <summary>Byter lösenord.</summary>
    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (dto.NewPassword != dto.ConfirmNewPassword)
            return BadRequest(new { Message = "Passwords do not match." });

        var userId = GetCurrentUserId();
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var result = await _userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

        await _auditLog.LogAsync(userId, "ChangePassword", "User", userId,
            "Password changed successfully", GetClientIp());

        return Ok(new { Message = "Password changed successfully." });
    }

    /// <summary>Hämtar användarens inställningar.</summary>
    [HttpGet("me/preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences()
    {
        var userId = GetCurrentUserId();
        var user = await _userRepository.GetUserDtoByIdAsync(userId);
        if (user is null) return NotFound();

        return Ok(user.Preferences ?? new UserPreferencesDto());
    }

    /// <summary>Uppdaterar användarens inställningar (tema, språk, notiser).</summary>
    [HttpPut("me/preferences")]
    [ProducesResponseType(typeof(UserPreferencesDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UserPreferencesDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userId = GetCurrentUserId();
        await _userRepository.UpdatePreferencesAsync(userId, dto);

        await _auditLog.LogAsync(userId, "UpdatePreferences", "User", userId,
            $"Language: {dto.Language}, Theme: {dto.Theme}", GetClientIp());

        return Ok(dto);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string GetCurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("User ID claim not found.");

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

/// <summary>DTO for updating own profile.</summary>
public class UpdateProfileDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
}
