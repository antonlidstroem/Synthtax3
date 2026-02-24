using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public List<string> Roles { get; set; } = new();
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public Guid TenantId { get; set; }
    public UserPreferencesDto? Preferences { get; set; }
    public List<string> AllowedModules { get; set; } = new();

    /// <summary>Primary role for display purposes.</summary>
    public string RoleDisplay => Roles.FirstOrDefault() ?? "User";
}

public class UserPreferencesDto
{
    public string Theme { get; set; } = "Light";
    public string Language { get; set; } = "sv-SE";
    public bool EmailNotifications { get; set; } = true;
    public bool ShowMetricsTrend { get; set; } = true;
    public int DefaultPageSize { get; set; } = 50;
}

public class RegisterDto
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

public class LoginDto
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserDto User { get; set; } = new();
}

public class RefreshTokenDto
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class AdminResetPasswordDto
{
    public string UserId { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class UpdateUserModulesDto
{
    public string UserId { get; set; } = string.Empty;
    public List<string> AllowedModules { get; set; } = new();
}

public class AdminCreateUserDto
{
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = "User";
}
