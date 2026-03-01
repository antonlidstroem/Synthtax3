using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Data.Interceptors;
using Synthtax.Infrastructure.Data.Seeders;
using Synthtax.Infrastructure.Services;

namespace Synthtax.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registrerar all infrastruktur för Fas 1-domänmodellen:
    /// DbContext, interceptorer, seed-tjänst och CurrentUserService.
    ///
    /// Anropas i Program.cs:
    /// <code>
    ///   builder.Services.AddDomainInfrastructure(builder.Configuration);
    /// </code>
    /// </summary>
    public static IServiceCollection AddDomainInfrastructure(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        // ── ICurrentUserService ───────────────────────────────────────────
        // Måste registreras innan DbContext för att kunna injiceras i interceptorn.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

        // ── Audit-interceptor ─────────────────────────────────────────────
        // Singleton — en instans delar alla DbContext-anrop.
        // Interceptorn beror på ICurrentUserService (Scoped), men hanterar
        // det korrekt via IServiceProvider om nödvändigt.
        services.AddSingleton<AuditSaveChangesInterceptor>();

        // ── DbContext ─────────────────────────────────────────────────────
        services.AddDbContext<SynthtaxDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:DefaultConnection saknas i konfigurationen.");

            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(SynthtaxDbContext).Assembly.GetName().Name);
                sql.EnableRetryOnFailure(
                    maxRetryCount:       5,
                    maxRetryDelay:       TimeSpan.FromSeconds(10),
                    errorNumbersToAdd:   null);
                sql.CommandTimeout(120);
            });

            // Koppla in audit-interceptorn
            var interceptor = sp.GetRequiredService<AuditSaveChangesInterceptor>();
            options.AddInterceptors(interceptor);

#if DEBUG
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
#endif
        });

        // ── RuleSeedService ───────────────────────────────────────────────
        // Synkroniserar plugin-regler med Rule-tabellen vid varje uppstart.
        services.AddHostedService<RuleSeedService>();

        return services;
    }
}
