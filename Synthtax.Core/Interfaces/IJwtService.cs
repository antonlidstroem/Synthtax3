using System.Security.Claims;
using Synthtax.Core.DTOs;

namespace Synthtax.Core.Interfaces;

public interface IJwtService
{
    string GenerateAccessToken(IEnumerable<Claim> claims);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
    DateTime GetAccessTokenExpiry();
}
