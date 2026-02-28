using Synthtax.Core.DTOs;
using Synthtax.Infrastructure.Entities;


namespace Synthtax.Core.Interfaces;

/// <summary>
/// Interface för användarhantering och refresh token-operationer.
/// Gör UserRepository testbar och löskopplad från controllers.
/// </summary>
public interface IUserRepository
{
    Task<UserDto?> GetUserDtoByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserDtoByNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAllUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UpdatePreferencesAsync(string userId, UserPreferencesDto prefsDto, CancellationToken cancellationToken = default);
    Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default);
    Task UpdateAllowedModulesAsync(string userId, List<string> modules, CancellationToken cancellationToken = default);
    Task<RefreshToken?> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(RefreshToken token, string? replacedBy = null, string? revokedByIp = null, CancellationToken cancellationToken = default);
    Task RevokeAllRefreshTokensForUserAsync(string userId, CancellationToken cancellationToken = default);
}
