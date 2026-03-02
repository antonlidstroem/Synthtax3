using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Application.SuperAdmin;
using Synthtax.Application.Telemetry;
using Synthtax.Application.Watchdog;
using Synthtax.Infrastructure.Services;

namespace Synthtax.API;

/// <summary>
/// DI-registrering för alla Fas 9-tjänster.
///
/// <para>Anropa <c>services.AddSuperAdmin(config)</c> i Program.cs.</para>
/// </summary>
public static class SuperAdminServiceExtensions
{
    public static IServiceCollection AddSuperAdmin(
        this IServiceCollection services,
        IConfiguration config)
    {
        // ── Org-hantering ──────────────────────────────────────────────────
        services.AddScoped<IOrgAdminService, OrgAdminService>();

        // ── Alert-tjänst ───────────────────────────────────────────────────
        services.AddScoped<IAlertService,    AlertServiceImpl>();
        services.AddScoped<IAdminAlertPublisher, AdminAlertHubPublisher>();
        services.AddScoped<IWatchdogRunRepository, WatchdogRunRepository>();

        // ── Global Telemetri ───────────────────────────────────────────────
        services.AddScoped<IGlobalHealthService, GlobalHealthService>();
        services.AddHostedService<TelemetryPurgeBackgroundService>();

        // ── Watchdog-checkers (registreras som IEnumerable<IWatchdogSourceChecker>) ─
        services.AddScoped<IWatchdogSourceChecker, VsReleaseChecker>();
        services.AddScoped<IWatchdogSourceChecker, AiModelChangelogChecker>();

        // ── Watchdog-bakgrundstjänst ──────────────────────────────────────
        services.AddHostedService<WatchdogBackgroundService>();

        // ── HTTP-klient för watchdog (med timeout och retry) ──────────────
        services.AddHttpClient("WatchdogClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Synthtax-Watchdog/1.0");
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        // ── SignalR: Admin-hub ─────────────────────────────────────────────
        // OBS: SignalR läggs till av AddSignalR() i Program.cs
        // Här lägger vi bara till AutoDI för AdminHub

        // ── Authorization-policy för SystemAdmin ──────────────────────────
        services.AddAuthorizationBuilder()
            .AddPolicy("SystemAdmin", policy =>
                policy.RequireAssertion(ctx =>
                {
                    var currentUser = ctx.Resource as Infrastructure.Services.ICurrentUserService;
                    return currentUser?.IsSystemAdmin == true;
                }));

        return services;
    }

    /// <summary>
    /// Mappning av AdminHub och telemetri-endpoints.
    /// Anropa i Program.cs efter <c>app.UseAuthorization()</c>.
    ///
    /// <code>
    /// app.MapSuperAdminEndpoints();
    /// </code>
    /// </summary>
    public static WebApplication MapSuperAdminEndpoints(this WebApplication app)
    {
        // SignalR Admin-hub (kräver SystemAdmin-auth)
        app.MapHub<AdminHub>("/hubs/admin", opts =>
        {
            opts.Transports =
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
        });

        return app;
    }
}
