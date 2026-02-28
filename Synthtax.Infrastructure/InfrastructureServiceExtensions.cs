using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Repositories;

namespace Synthtax.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── SQL Server (huvud-databas) ─────────────────────────────────────────
        services.AddDbContext<SynthtaxDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly("Synthtax.Infrastructure")));

        // ── SQLite (analyscache) ──────────────────────────────────────────────
        services.AddDbContext<AnalysisCacheDbContext>(options =>
            options.UseSqlite("Data Source=synthtax_cache.db"));

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<IAnalysisCacheService,  AnalysisCacheRepository>();
        services.AddScoped<IBacklogRepository,     BacklogRepository>();
        services.AddScoped<IAuditLogRepository,    AuditLogRepository>();

        // IUserRepository registreras nu korrekt via interface
        services.AddScoped<IUserRepository,        UserRepository>();

        // Behåll direkt-registrering för bakåtkompatibilitet tills alla controllers är uppdaterade
        // (kan tas bort när AdminController, AuthController och UsersController är deployade)
        services.AddScoped<UserRepository>(sp => (UserRepository)sp.GetRequiredService<IUserRepository>());

        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        return services;
    }
}
