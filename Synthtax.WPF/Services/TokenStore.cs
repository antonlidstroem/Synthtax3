using Synthtax.Core.DTOs;

namespace Synthtax.WPF.Services;

/// <summary>
/// Holds JWT tokens in memory for the lifetime of the application.
/// Tokens are never written to disk – only in-memory for security.
/// </summary>
public class TokenStore
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public UserDto? CurrentUser { get; set; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public bool IsAdmin => CurrentUser?.Roles.Contains("Admin") ?? false;

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        CurrentUser = null;
    }
}
