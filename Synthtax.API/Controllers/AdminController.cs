using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public class AdminController : SynthtaxControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserRepository _userRepository;     // ← interface, inte konkret klass
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        IUserRepository userRepository,
        IAuditLogRepository auditLog,
        ILogger<AdminController> logger)
    {
        _userManager    = userManager;
        _userRepository = userRepository;
        _auditLog       = auditLog;
        _logger         = logger;
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(List<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userRepository.GetAllUsersAsync(GetTenantId());
        return Ok(users);
    }

    [HttpGet("users/{userId}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(string userId)
    {
        var user = await _userRepository.GetUserDtoByIdAsync(userId);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost("users")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateUser([FromBody] AdminCreateUserDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = new ApplicationUser
        {
            UserName       = dto.UserName,
            Email          = dto.Email,
            FullName       = dto.FullName,
            EmailConfirmed = true,
            IsActive       = true,
            TenantId       = GetTenantId(),
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

        var role = dto.Role is "Admin" ? "Admin" : "User";
        await _userManager.AddToRoleAsync(user, role);

        await _auditLog.LogAsync(
            GetCurrentUserId(), "AdminCreateUser", "User", user.Id,
            $"Created user: {user.UserName} with role: {role}",
            GetClientIp(), tenantId: GetTenantId());

        var userDto = await _userRepository.GetUserDtoByIdAsync(user.Id);
        return CreatedAtAction(nameof(GetUser), new { userId = user.Id }, userDto);
    }

    [HttpPatch("users/{userId}/active")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetUserActive(string userId, [FromBody] SetActiveDto dto)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        user.IsActive = dto.IsActive;
        await _userManager.UpdateAsync(user);

        await _auditLog.LogAsync(
            GetCurrentUserId(), dto.IsActive ? "ActivateUser" : "DeactivateUser",
            "User", userId, $"User: {user.UserName}", GetClientIp(), tenantId: GetTenantId());

        return Ok(new { Message = $"User {(dto.IsActive ? "activated" : "deactivated")}." });
    }

    [HttpDelete("users/{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteUser(string userId)
    {
        if (userId == GetCurrentUserId())
            return BadRequest(new { Message = "Cannot delete your own account." });

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var userName = user.UserName;
        var result   = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

        await _auditLog.LogAsync(
            GetCurrentUserId(), "DeleteUser", "User", userId,
            $"Deleted user: {userName}", GetClientIp(), tenantId: GetTenantId());

        return Ok(new { Message = $"User '{userName}' deleted." });
    }

    [HttpPost("users/reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword([FromBody] AdminResetPasswordDto dto)
    {
        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user is null) return NotFound();

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result     = await _userManager.ResetPasswordAsync(user, resetToken, dto.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

        await _userRepository.RevokeAllRefreshTokensForUserAsync(user.Id);

        await _auditLog.LogAsync(
            GetCurrentUserId(), "ResetPassword", "User", user.Id,
            $"Password reset for: {user.UserName}", GetClientIp(), tenantId: GetTenantId());

        return Ok(new { Message = "Password reset successfully. All existing sessions revoked." });
    }

    [HttpPatch("users/{userId}/modules")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserModules(
        string userId, [FromBody] UpdateUserModulesDto dto)
    {
        if (!string.IsNullOrWhiteSpace(userId))
            dto.UserId = userId;

        var user = await _userManager.FindByIdAsync(dto.UserId);
        if (user is null) return NotFound();

        await _userRepository.UpdateAllowedModulesAsync(dto.UserId, dto.AllowedModules);

        await _auditLog.LogAsync(
            GetCurrentUserId(), "UpdateModuleAccess", "User", dto.UserId,
            $"Modules set to: {string.Join(", ", dto.AllowedModules)}",
            GetClientIp(), tenantId: GetTenantId());

        return Ok(new { Message = "Module access updated." });
    }

    [HttpPut("users/{userId}/role")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateUserRole(string userId, [FromBody] UpdateRoleDto dto)
    {
        if (userId == GetCurrentUserId())
            return BadRequest(new { Message = "Cannot change your own role." });

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);

        var newRole = dto.Role is "Admin" ? "Admin" : "User";
        await _userManager.AddToRoleAsync(user, newRole);

        await _auditLog.LogAsync(
            GetCurrentUserId(), "UpdateRole", "User", userId,
            $"Role changed to: {newRole} for {user.UserName}",
            GetClientIp(), tenantId: GetTenantId());

        return Ok(new { Message = $"Role updated to '{newRole}'." });
    }

    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(PagedResultDto<AuditLogDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditLog([FromQuery] AuditLogQueryDto query)
    {
        var result = await _auditLog.GetPagedAsync(query, GetTenantId());
        return Ok(result);
    }
}
