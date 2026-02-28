using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Synthtax.API.Controllers;

/// <summary>
/// Bas-controller med delade hjälpmetoder för claim-extraktion.
/// Ersätter duplicerad GetCurrentUserId/GetTenantId/GetClientIp i alla controllers.
/// </summary>
public abstract class SynthtaxControllerBase : ControllerBase
{
    /// <summary>
    /// Hämtar inloggad användares ID från JWT-claimet.
    /// Kastar <see cref="UnauthorizedAccessException"/> om claimet saknas.
    /// </summary>
    protected string GetCurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("User ID claim not found in token.");

    /// <summary>
    /// Hämtar inloggad användares tenant-ID från JWT-claimet.
    /// Returnerar <see cref="Guid.Empty"/> om claimet saknas eller är ogiltigt.
    /// </summary>
    protected Guid GetTenantId()
    {
        var claim = User.FindFirstValue("tenant_id");
        return claim is not null && Guid.TryParse(claim, out var guid)
            ? guid
            : Guid.Empty;
    }

    /// <summary>
    /// Hämtar klientens IP-adress från HTTP-kontexten.
    /// </summary>
    protected string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Returnerar true om den inloggade användaren har Admin-rollen.
    /// </summary>
    protected bool IsAdmin()
        => User.IsInRole("Admin");

    /// <summary>
    /// Hämtar användarens visningsnamn (full_name eller username).
    /// </summary>
    protected string GetDisplayName()
        => User.FindFirstValue("full_name")
           ?? User.FindFirstValue(ClaimTypes.Name)
           ?? "Unknown";
}
