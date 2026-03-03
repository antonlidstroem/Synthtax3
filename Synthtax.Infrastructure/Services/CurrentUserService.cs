using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Synthtax.Infrastructure.Services;

public interface ICurrentUserService
{
    string? UserId        { get; }
    bool    IsSystemAdmin { get; }
}

public sealed class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;
    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public HttpContextCurrentUserService(IHttpContextAccessor http)
    {
        _http = http;
    }

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsSystemAdmin =>
        Principal?.IsInRole("SystemAdmin") ?? false;
}

/// <summary>
/// Used by background services that run outside an HTTP request context.
/// </summary>
public sealed class SystemCurrentUserService : ICurrentUserService
{
    public string? UserId        => null;
    public bool    IsSystemAdmin => true;
}
