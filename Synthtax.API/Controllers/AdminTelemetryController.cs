using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Filters;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Application.Telemetry;

namespace Synthtax.API.Controllers;

/// <summary>
/// API för anonymiserad plugin-telemetri och global hälsoöversikt.
///
/// <para><b>Routing:</b>
/// <list type="bullet">
///   <item><c>POST api/v1/telemetry/ingest</c> — öppen för alla autentiserade VSIX (ej admin-only).</item>
///   <item><c>GET api/v1/admin/health</c> — aggregerad vy, kräver super-admin.</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class AdminTelemetryController : ControllerBase
{
    private readonly IGlobalHealthService _health;

    public AdminTelemetryController(IGlobalHealthService health) => _health = health;

    // ── POST /api/v1/telemetry/ingest ─────────────────────────────────────
    /// <summary>
    /// Tar emot hälsorapport från ett installerat VSIX-plugin.
    ///
    /// <para>Öppen för alla autentiserade JWT-användare — VSIX skickar
    /// hit utan att användaren ens märker det (bakgrundsanrop).</para>
    ///
    /// <para>Returen är alltid 204 — vi avslöjar aldrig fel för att
    /// undvika att plugins spammar om det uppstår valideringsfel.</para>
    /// </summary>
    [Authorize]
    [HttpPost("api/v1/telemetry/ingest")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Ingest(
        [FromBody] TelemetryIngestRequest request,
        CancellationToken ct = default)
    {
        // Tyst accept — fel loggas internt, exponeras aldrig till klienten
        try { await _health.IngestAsync(request, ct); }
        catch { /* Loggas av GlobalHealthService */ }

        return NoContent();
    }

    // ── GET /api/v1/admin/health ──────────────────────────────────────────
    /// <summary>
    /// Hämtar aggregerad global hälsoöversikt baserad på telemetri
    /// från alla installerade plugins under de senaste N dagarna.
    ///
    /// <para>Kräver super-admin.</para>
    /// </summary>
    [Authorize]
    [RequireSystemAdmin]
    [HttpGet("api/v1/admin/health")]
    [ProducesResponseType(typeof(GlobalHealthDto), 200)]
    public async Task<IActionResult> GetGlobalHealth(
        [FromQuery] int lookbackDays = 7,
        CancellationToken ct = default)
    {
        var clamped = Math.Clamp(lookbackDays, 1, 90);
        var health  = await _health.GetGlobalHealthAsync(clamped, ct);
        return Ok(health);
    }

    // ── GET /api/v1/admin/health/version-matrix ───────────────────────────
    /// <summary>
    /// Kryssmatris: plugin-version × VS-version → antal aktiva installationer.
    /// Hjälper att prioritera vilka kombinationer att testa.
    /// </summary>
    [Authorize]
    [RequireSystemAdmin]
    [HttpGet("api/v1/admin/health/version-matrix")]
    [ProducesResponseType(typeof(VersionMatrixDto), 200)]
    public async Task<IActionResult> GetVersionMatrix(
        [FromQuery] int lookbackDays = 14,
        CancellationToken ct = default)
    {
        var health = await _health.GetGlobalHealthAsync(
            Math.Clamp(lookbackDays, 1, 90), ct);

        // Bygg matrisen från distributions
        var matrix = new VersionMatrixDto
        {
            PluginVersions   = health.VersionDistribution,
            VsVersions       = health.VsVersionDistribution,
            TotalInstalls    = health.ActiveInstallations,
            GeneratedAt      = health.GeneratedAt
        };

        return Ok(matrix);
    }
}

/// <summary>Plugin × VS version-matris för compatibility-planering.</summary>
public sealed record VersionMatrixDto
{
    public IReadOnlyDictionary<string, int> PluginVersions { get; init; }
        = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> VsVersions     { get; init; }
        = new Dictionary<string, int>();
    public int      TotalInstalls { get; init; }
    public DateTime GeneratedAt   { get; init; }
}
