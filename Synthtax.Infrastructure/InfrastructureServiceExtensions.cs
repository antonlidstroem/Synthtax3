using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Repositories;

namespace Synthtax.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Main SQL Server database (unchanged) ─────────────────────────────
        services.AddDbContext<SynthtaxDbContext>(options =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql =>
                {
                    sql.MigrationsAssembly(typeof(SynthtaxDbContext).Assembly.FullName);
                    sql.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);
                });
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                options.EnableSensitiveDataLogging().EnableDetailedErrors();
        });

        // ── NEW: SQLite analysis cache database ──────────────────────────────
        // Stored in a configurable path; defaults to the app's data directory.
        var cachePath = configuration["AnalysisCache:DatabasePath"]
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Synthtax",
                "analysis-cache.db");

        // Ensure directory exists
        var cacheDir = Path.GetDirectoryName(cachePath)!;
        Directory.CreateDirectory(cacheDir);

        services.AddDbContext<AnalysisCacheDbContext>(options =>
        {
            options.UseSqlite($"Data Source={cachePath}");
        });

        services.AddScoped<IAnalysisCacheService, AnalysisCacheRepository>();

        // ── Existing repositories (unchanged) ────────────────────────────────
        services.AddScoped<IBacklogRepository, BacklogRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<UserRepository>();
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        return services;
    }
}
