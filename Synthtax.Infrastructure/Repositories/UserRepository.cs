using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Repositories;

/// <summary>
/// Implementerar IUserRepository. Hanterar användarprofiler och refresh tokens.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly SynthtaxDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public UserRepository(SynthtaxDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<UserDto?> GetUserDtoByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return MapToDto(user, roles);
    }

    public async Task<UserDto?> GetUserDtoByNameAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users
            .Include(u => u.Preferences)
            .FirstOrDefaultAsync(u => u.NormalizedUserName == userName.ToUpperInvariant(), cancellationToken);
        if (user is null) return null;
        var roles = await _userManager.GetRolesAsync(user);
        return MapToDto(user, roles);
    }

    public async Task<List<UserDto>> GetAllUsersAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var users = await _context.Users
            .Include(u => u.Preferences)
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.UserName)
            .ToListAsync(cancellationToken);

        var result = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(MapToDto(user, roles));
        }
        return result;
    }

    public async Task UpdatePreferencesAsync(
        string userId,
        UserPreferencesDto prefsDto,
        CancellationToken cancellationToken = default)
    {
        var prefs = await _context.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (prefs is null)
        {
            prefs = new UserPreference { UserId = userId };
            _context.UserPreferences.Add(prefs);
        }

        prefs.Theme              = prefsDto.Theme;
        prefs.Language           = prefsDto.Language;
        prefs.EmailNotifications = prefsDto.EmailNotifications;
        prefs.ShowMetricsTrend   = prefsDto.ShowMetricsTrend;
        prefs.DefaultPageSize    = prefsDto.DefaultPageSize;
        prefs.UpdatedAt          = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateLastLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user is null) return;
        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAllowedModulesAsync(
        string userId,
        List<string> modules,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user is null) return;
        user.AllowedModules = modules.Count > 0 ? string.Join(",", modules) : null;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
        => await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == token, cancellationToken);

    public async Task AddRefreshTokenAsync(RefreshToken token, CancellationToken cancellationToken = default)
    {
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeRefreshTokenAsync(
        RefreshToken token,
        string? replacedBy = null,
        string? revokedByIp = null,
        CancellationToken cancellationToken = default)
    {
        token.RevokedAt       = DateTime.UtcNow;
        token.ReplacedByToken = replacedBy;
        token.RevokedByIp     = revokedByIp;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAllRefreshTokensForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var tokens = await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
            token.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static UserDto MapToDto(ApplicationUser user, IList<string> roles) => new()
    {
        Id           = user.Id,
        UserName     = user.UserName ?? string.Empty,
        Email        = user.Email    ?? string.Empty,
        FullName     = user.FullName,
        Roles        = roles.ToList(),
        IsActive     = user.IsActive,
        CreatedAt    = user.CreatedAt,
        LastLoginAt  = user.LastLoginAt,
        TenantId     = user.TenantId,
        AllowedModules = string.IsNullOrEmpty(user.AllowedModules)
            ? new List<string>()
            : user.AllowedModules.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
        Preferences = user.Preferences is null ? null : new UserPreferencesDto
        {
            Theme              = user.Preferences.Theme,
            Language           = user.Preferences.Language,
            EmailNotifications = user.Preferences.EmailNotifications,
            ShowMetricsTrend   = user.Preferences.ShowMetricsTrend,
            DefaultPageSize    = user.Preferences.DefaultPageSize
        }
    };
}
