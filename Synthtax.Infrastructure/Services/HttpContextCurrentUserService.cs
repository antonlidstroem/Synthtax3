using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Synthtax.Infrastructure.Data.Interceptors;

namespace Synthtax.Infrastructure.Services;

/// <summary>
/// Implementerar <see cref="ICurrentUserService"/> genom att läsa
/// inloggad användare från HTTP-kontexten.
/// Registreras som Scoped — en instans per request.
/// </summary>
public sealed class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Returnerar NameIdentifier-claim (ApplicationUser.Id) om användaren är autentiserad,
    /// annars null (bakgrundsjobb, seed etc. skriver som "system" via DbContext-fallback).
    /// </summary>
    public string? UserId =>
        _httpContextAccessor.HttpContext?.User?
            .FindFirstValue(ClaimTypes.NameIdentifier);
}
