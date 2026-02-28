namespace Synthtax.Core.DTOs;

/// <summary>
/// Carries the stored refresh token data through the IUserRepository boundary.
/// Named "Info" to avoid conflict with RefreshTokenDto (the request-body DTO for POST /refresh).
/// Infrastructure's UserRepository maps between this and the RefreshToken EF entity.
/// </summary>
public class RefreshTokenInfoDto
{
    public Guid     Id              { get; set; }
    public string   Token           { get; set; } = string.Empty;
    public string   UserId          { get; set; } = string.Empty;
    public DateTime ExpiresAt       { get; set; }
    public DateTime CreatedAt       { get; set; }
    public string?  CreatedByIp     { get; set; }
    public DateTime? RevokedAt      { get; set; }
    public string?  RevokedByIp     { get; set; }
    public string?  ReplacedByToken { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt is not null;
    public bool IsActive  => !IsRevoked && !IsExpired;
}
