using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Synthtax.Infrastructure.Services;    // ICurrentUserService (Fas 5)

namespace Synthtax.API.Filters;

// ═══════════════════════════════════════════════════════════════════════════
// [RequireSystemAdmin]  — Action-attribut för super-admin endpoints
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Begränsar en controller eller action till super-admins.
///
/// <para>Kontrollerar <c>ICurrentUserService.IsSystemAdmin</c> (Fas 5)
/// efter att standard-JWT-autentisering kört klart. Returnerar 403
/// med ett maskinläsbart fel om anroparen inte är super-admin.</para>
///
/// <code>
/// [RequireSystemAdmin]
/// [ApiController]
/// [Route("api/v1/admin/orgs")]
/// public sealed class AdminOrgController : ControllerBase { ... }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireSystemAdminAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var currentUser = context.HttpContext.RequestServices
            .GetService(typeof(ICurrentUserService)) as ICurrentUserService;

        if (currentUser is null || !currentUser.IsSystemAdmin)
        {
            context.Result = new ObjectResult(new
            {
                error   = "ACCESS_DENIED",
                message = "This endpoint requires system administrator privileges."
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Hjälptyper för API-lager
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Gemensamma pagineringsparametrar för alla list-endpoints.</summary>
public sealed class PaginationParams
{
    private int _page     = 1;
    private int _pageSize = 25;

    public int Page
    {
        get => _page;
        set => _page = Math.Max(1, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, 100);
    }
}

/// <summary>Generiskt API-svarskuvert med metadata.</summary>
public sealed record ApiResponse<T>
{
    public required T    Data      { get; init; }
    public bool          Success   { get; init; } = true;
    public DateTime      Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data) => new() { Data = data };
}
