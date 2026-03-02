using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Filters;
using Synthtax.Application.SuperAdmin;
using Synthtax.Application.SuperAdmin.DTOs;

namespace Synthtax.API.Controllers;

/// <summary>
/// Super-admin REST-API för organisations-hantering.
///
/// <para><b>Behörighet:</b> Kräver <c>[Authorize]</c> + <c>[RequireSystemAdmin]</c>.
/// Alla endpoints returnerar 403 om anroparen inte är super-admin.</para>
///
/// <para><b>Routing:</b> <c>api/v1/admin/orgs</c></para>
///
/// <para><b>Tenant-isolation:</b>
/// Alla queries kör på <c>IgnoreQueryFilters()</c> — super-admin ser
/// och kan ändra alla organisationer oavsett tenant.</para>
/// </summary>
[Authorize]
[RequireSystemAdmin]
[ApiController]
[Route("api/v1/admin/orgs")]
[Produces("application/json")]
public sealed class AdminOrgController : ControllerBase
{
    private readonly IOrgAdminService _orgs;

    public AdminOrgController(IOrgAdminService orgs) => _orgs = orgs;

    // ── GET /api/v1/admin/orgs ─────────────────────────────────────────────
    /// <summary>
    /// Hämtar paginerad lista av alla organisationer.
    /// Stöder sökning på namn/slug/e-post, filtrering på plan och aktiv-status.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(OrgListResponse), 200)]
    public async Task<IActionResult> ListOrgs(
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 25,
        [FromQuery] string? search     = null,
        [FromQuery] string? plan       = null,
        [FromQuery] bool?   activeOnly = null,
        CancellationToken ct = default)
    {
        var result = await _orgs.ListOrgsAsync(
            page, pageSize, search, plan, activeOnly, ct);
        return Ok(result);
    }

    // ── GET /api/v1/admin/orgs/{id} ────────────────────────────────────────
    /// <summary>Hämtar fullständig vy av en specifik organisation.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrgAdminDto), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetOrg(Guid id, CancellationToken ct = default)
    {
        try
        {
            var org = await _orgs.GetOrgAsync(id, ct);
            return Ok(org);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── POST /api/v1/admin/orgs ────────────────────────────────────────────
    /// <summary>
    /// Skapar en ny organisation med angiven plan, licensantal och features.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrgAdminDto), 201)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<IActionResult> CreateOrg(
        [FromBody] CreateOrgRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var org = await _orgs.CreateOrgAsync(request, ct);
            return CreatedAtAction(nameof(GetOrg), new { id = org.Id }, org);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("taken"))
        {
            return Conflict(new ProblemDetails
            {
                Title  = "Slug already taken",
                Detail = ex.Message,
                Status = 409
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title  = "Invalid request",
                Detail = ex.Message,
                Status = 400
            });
        }
    }

    // ── PATCH /api/v1/admin/orgs/{id} ──────────────────────────────────────
    /// <summary>
    /// Uppdaterar organisation (partial update — null-fält ignoreras).
    /// Kan ändra plan, licensantal, features och aktiv-status.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(OrgAdminDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> UpdateOrg(
        Guid id,
        [FromBody] UpdateOrgRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var org = await _orgs.UpdateOrgAsync(id, request, ct);
            return Ok(org);
        }
        catch (KeyNotFoundException)   { return NotFound(); }
        catch (ArgumentException ex)   { return BadRequest(new ProblemDetails
            { Title = "Invalid request", Detail = ex.Message, Status = 400 }); }
    }

    // ── POST /api/v1/admin/orgs/{id}/deactivate ───────────────────────────
    /// <summary>
    /// Inaktiverar en organisation. Befintliga members kan inte logga in.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeactivateOrg(
        Guid id,
        [FromBody] DeactivateOrgRequest request,
        CancellationToken ct = default)
    {
        try
        {
            await _orgs.DeactivateOrgAsync(id, request.Reason, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── POST /api/v1/admin/orgs/{id}/reactivate ───────────────────────────
    /// <summary>Återaktiverar en inaktiv organisation.</summary>
    [HttpPost("{id:guid}/reactivate")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ReactivateOrg(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            await _orgs.ReactivateOrgAsync(id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    // ── GET /api/v1/admin/orgs/stats ──────────────────────────────────────
    /// <summary>Snabbstatistik: antal orgs per plan, aktiva vs inaktiva.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(OrgStatsResponse), 200)]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        var total = await _orgs.GetOrgCountAsync(ct);
        return Ok(new OrgStatsResponse { TotalOrgs = total });
    }
}

/// <summary>Body för deaktivera-anrop.</summary>
public sealed record DeactivateOrgRequest
{
    public required string Reason { get; init; }
}

/// <summary>Enkel statistik-vy.</summary>
public sealed record OrgStatsResponse
{
    public int TotalOrgs { get; init; }
}
