using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Data.Interceptors;
using Synthtax.Infrastructure.Repositories;
using Synthtax.Infrastructure.Services;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddSynthtaxInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<AuditSaveChangesInterceptor>();

        // SQL Server - Huvuddatabas
        var connString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection saknas.");

        services.AddDbContext<SynthtaxDbContext>((sp, options) =>
        {
            options.UseSqlServer(connString, sql => sql.MigrationsAssembly("Synthtax.Infrastructure"));
            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        // SQLite - Analyscache
        services.AddDbContext<AnalysisCacheDbContext>(options =>
            options.UseSqlite("Data Source=synthtax_cache.db"));

        // Repositories
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));
        services.AddScoped<IAnalysisCacheService, AnalysisCacheRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Bakåtkompatibilitet
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

        return services;
    }
}