using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IUserRepository
{
    Task<UserDto?> GetUserDtoByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserDto?> GetUserDtoByNameAsync(string userName, CancellationToken cancellationToken = default);
    Task<List<UserDto>> GetAllUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task UpdatePreferencesAsync(string userId, UserPreferencesDto prefsDto, CancellationToken cancellationToken = default);
    Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default);
    Task UpdateAllowedModulesAsync(string userId, List<string> modules, CancellationToken cancellationToken = default);

    // Refresh token management – uses RefreshTokenInfoDto (Core), not the EF entity (Infrastructure)
    Task<RefreshTokenInfoDto?> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task AddRefreshTokenAsync(RefreshTokenInfoDto token, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(RefreshTokenInfoDto token, string? replacedBy = null, string? revokedByIp = null, CancellationToken cancellationToken = default);
    Task RevokeAllRefreshTokensForUserAsync(string userId, CancellationToken cancellationToken = default);
}
