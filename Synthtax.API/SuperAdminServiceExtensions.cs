using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Application.SuperAdmin;
using Synthtax.Application.Telemetry;
using Synthtax.Application.Watchdog;
using Synthtax.API.Hubs;
using Synthtax.Infrastructure.Repositories;
using Synthtax.Infrastructure.Services;

namespace Synthtax.API;

public static class SuperAdminServiceExtensions
{
    public static IServiceCollection AddSuperAdmin(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddScoped<IOrgAdminService,       OrgAdminService>();
        services.AddScoped<IAlertService,          AlertServiceImpl>();
        services.AddScoped<IAdminAlertPublisher,   AdminAlertHubPublisher>();
        services.AddScoped<IWatchdogRunRepository, WatchdogRunRepository>();
        services.AddScoped<IGlobalHealthService,   GlobalHealthService>();

        services.AddScoped<IWatchdogSourceChecker, VsReleaseChecker>();
        services.AddScoped<IWatchdogSourceChecker, AiModelChangelogChecker>();

        services.AddHostedService<WatchdogBackgroundService>();

        services.AddHttpClient("WatchdogClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Synthtax-Watchdog/1.0");
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddAuthorizationBuilder()
            .AddPolicy("SystemAdmin", policy =>
                policy.RequireAssertion(ctx =>
                {
                    var currentUser = ctx.Resource as ICurrentUserService;
                    return currentUser?.IsSystemAdmin == true;
                }));

        return services;
    }

    public static WebApplication MapSuperAdminEndpoints(this WebApplication app)
    {
        app.MapHub<AdminHub>("/hubs/admin", opts =>
        {
            opts.Transports =
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
        });

        return app;
    }
}
