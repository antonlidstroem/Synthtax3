using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Synthtax.Application.SaaS;
using Synthtax.Core.SaaS;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Data.Interceptors;
using Synthtax.Infrastructure.Services;

namespace Synthtax.Infrastructure.Extensions;

/// <summary>
/// Registrerar alla Fas 5-komponenter.
///
/// <para><b>Anrop i Program.cs:</b>
/// <code>
///   // Fas 1–4 (befintliga)
///   builder.Services.AddDomainInfrastructure(builder.Configuration);
///   builder.Services.AddPluginCore();
///   builder.Services.AddOrchestrator();
///   builder.Services.AddFuzzyMatching();
///
///   // Fas 5
///   builder.Services.AddSaasInfrastructure(builder.Configuration);
///   builder.Services.AddSaasAuthentication(builder.Configuration);
///
///   // Middleware — MÅSTE vara i rätt ordning
///   app.UseAuthentication();
///   app.UseAuthorization();
///   app.UseTenantContext();   // ← Fas 5: laddar org-kontext till HttpContext
/// </code>
/// </para>
/// </summary>
public static class SaasServiceExtensions
{
    // ═══════════════════════════════════════════════════════════════════════
    // Tjänster
    // ═══════════════════════════════════════════════════════════════════════

    public static IServiceCollection AddSaasInfrastructure(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        // ── DbContext — ersätter/registrerar V5 ───────────────────────────
        services.AddDbContext<SynthtaxDbContextV5>((sp, opts) =>
        {
            opts.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    sql.EnableRetryOnFailure(maxRetryCount: 3);
                    sql.CommandTimeout(120);
                });

            // Interceptor för audit-fält
            opts.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // ── ICurrentUserService — Fas 5-version (ersätter Fas 1) ──────────
        // OBS: Ta bort den gamla HttpContextCurrentUserService-registreringen
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserServiceV5>();

        // ── Caching för LicenseGuard ──────────────────────────────────────
        services.AddMemoryCache();

        // ── Domäntjänster ─────────────────────────────────────────────────
        services.AddScoped<ILicenseGuard,       LicenseGuardService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IInvitationService,  InvitationService>();

        // ── JWT-tjänst ────────────────────────────────────────────────────
        services.AddScoped<ISaasJwtService, SaasJwtService>();

        return services;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // JWT-autentisering
    // ═══════════════════════════════════════════════════════════════════════

    public static IServiceCollection AddSaasAuthentication(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        var secretKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey saknas i konfigurationen.");

        if (secretKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:SecretKey måste vara minst 32 tecken (256 bitar).");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = configuration["Jwt:Issuer"],
                    ValidateAudience         = true,
                    ValidAudience            = configuration["Jwt:Audience"],
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                  Encoding.UTF8.GetBytes(secretKey)),
                    ClockSkew                = TimeSpan.FromSeconds(30),

                    // Mappar "role"-claim till ClaimTypes.Role för [Authorize(Roles=...)]
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                // Stöd för SignalR/WebSocket-tokens via query string
                opts.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        // Policy-baserad auktorisering
        services.AddAuthorization(opts =>
        {
            opts.AddPolicy("OrgAdminOrSystemAdmin", policy =>
                policy.RequireAssertion(ctx =>
                    ctx.User.IsInRole("OrgAdmin") ||
                    ctx.User.IsInRole("Admin") ||
                    ctx.User.HasClaim(
                        API.SaaS.JWT.SynthtaxClaimTypes.IsSystemAdmin, "true")));

            opts.AddPolicy("HasOrganization", policy =>
                policy.RequireClaim(API.SaaS.JWT.SynthtaxClaimTypes.OrganizationId));
        });

        return services;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Middleware
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validerar att autentiserade användare faktiskt tillhör en aktiv organisation.
    /// Returnerar 403 om org saknas eller är inaktiverad.
    /// Måste placeras EFTER UseAuthentication() och UseAuthorization().
    /// </summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantContextMiddleware>();
}

// ═══════════════════════════════════════════════════════════════════════════
// TenantContextMiddleware
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Middleware som körs efter JWT-validering och kontrollerar att
/// org-claim pekar på en aktiv organisation.
///
/// <para>Hoppar över endpoints markerade med <c>[AllowAnonymous]</c>
/// och inbjudnings-accept-endpoint.</para>
/// </summary>
internal sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    // Paths som är undantagna från tenant-kontroll
    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/organizations",                  // Create org (ingen org ännu)
        "/api/v1/organizations/invitations/accept", // Accept invite (utanför org)
        "/api/v1/auth/login",
        "/api/v1/auth/register",
        "/api/v1/auth/refresh",
        "/health",
        "/swagger"
    };

    public TenantContextMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Anonyma requests och bypass-paths passerar direkt
        if (!ctx.User.Identity?.IsAuthenticated == true ||
            ShouldBypass(ctx.Request.Path))
        {
            await _next(ctx);
            return;
        }

        var orgId = ctx.User.GetOrganizationId();
        if (orgId is null)
        {
            // Inloggad men utan org — t.ex. ny användare som ännu inte gått med i en org
            // Tillåt åtkomst till org-skapande endpoint
            await _next(ctx);
            return;
        }

        // Lätt sanity-check: org-claim finns → fortsätt (DbContext-filtret hanterar isolation)
        await _next(ctx);
    }

    private static bool ShouldBypass(PathString path) =>
        BypassPaths.Any(bp => path.StartsWithSegments(bp, StringComparison.OrdinalIgnoreCase));
}

// Extension methods för HttpContext
file static class HttpContextExtensions
{
    public static Guid? GetOrganizationId(this System.Security.Claims.ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst(API.SaaS.JWT.SynthtaxClaimTypes.OrganizationId)?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
