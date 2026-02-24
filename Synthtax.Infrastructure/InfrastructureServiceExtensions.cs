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
        // ── DbContext ─────────────────────────────────────────────────────
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

            // Visa SQL i development
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                options.EnableSensitiveDataLogging().EnableDetailedErrors();
        });

        // ── ASP.NET Core Identity ─────────────────────────────────────────
        //services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        //{
        //    // Lösenordspolicy
        //    options.Password.RequireDigit = true;
        //    options.Password.RequireLowercase = true;
        //    options.Password.RequireUppercase = true;
        //    options.Password.RequireNonAlphanumeric = true;
        //    options.Password.RequiredLength = 8;

        //    // Låsningspolicy
        //    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        //    options.Lockout.MaxFailedAccessAttempts = 5;
        //    options.Lockout.AllowedForNewUsers = true;

        //    // Användarpolicy
        //    options.User.RequireUniqueEmail = true;
        //    options.SignIn.RequireConfirmedEmail = false; // Enklare onboarding i v1
        //})
        //.AddEntityFrameworkStores<SynthtaxDbContext>()
        //.AddDefaultTokenProviders();

        


        // ── Repositories ─────────────────────────────────────────────────
        services.AddScoped<IBacklogRepository, BacklogRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<UserRepository>();

        // Generiska repositories (registreras öppet generiskt)
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        return services;
    }
}
