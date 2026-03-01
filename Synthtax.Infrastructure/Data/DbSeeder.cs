using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider)
    {
        using var scope   = serviceProvider.CreateScope();
        var services      = scope.ServiceProvider;
        var logger        = services.GetRequiredService<ILogger<SynthtaxDbContext>>();
        var config        = services.GetRequiredService<IConfiguration>();

        try
        {
            var context = services.GetRequiredService<SynthtaxDbContext>();
            await context.Database.MigrateAsync();
            logger.LogInformation("Database migration applied successfully.");

            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

            await SeedRolesAsync(roleManager, logger);
            await SeedAdminUserAsync(userManager, config, logger);
            await SeedDemoUserAsync(userManager, config, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    private static async Task SeedRolesAsync(
        RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        foreach (var role in new[] { "Admin", "User" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (result.Succeeded)
                    logger.LogInformation("Role '{Role}' created.", role);
            }
        }
    }

    private static async Task SeedAdminUserAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        const string userName = "admin";
        if (await userManager.FindByNameAsync(userName) is not null)
        {
            logger.LogInformation("Admin user already exists – skipping.");
            return;
        }

        var password = config["Seeding:AdminPassword"] ?? "Admin@Synthtax1!";
        var user = new ApplicationUser
        {
            UserName       = userName,
            Email          = "admin@synthtax.local",
            FullName       = "System Administrator",
            EmailConfirmed = true,
            IsActive       = true,
            TenantId       = Guid.Empty,
            CreatedAt      = DateTime.UtcNow,
            Preferences    = DefaultPreferences("sv-SE"),
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "Admin");

            // SEC-01 FIX: Lösenordet loggades tidigare i klartext:
            //   logger.LogInformation("... Password: {Password}", userName, password);
            // Det är borttaget. Lösenordet sätts via Seeding:AdminPassword i miljövariabler.
            logger.LogInformation("Admin user '{User}' created successfully.", userName);
        }
        else
        {
            logger.LogError("Failed to create admin user: {Errors}",
                string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task SeedDemoUserAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger)
    {
        const string userName = "demo";
        if (await userManager.FindByNameAsync(userName) is not null) return;

        var password = config["Seeding:DemoPassword"] ?? "Demo@Synthtax1!";
        var user = new ApplicationUser
        {
            UserName       = userName,
            Email          = "demo@synthtax.local",
            FullName       = "Demo User",
            EmailConfirmed = true,
            IsActive       = true,
            TenantId       = Guid.Empty,
            CreatedAt      = DateTime.UtcNow,
            Preferences    = DefaultPreferences("sv-SE"),
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, "User");

            // SEC-01 FIX: Lösenord loggas inte.
            logger.LogInformation("Demo user '{User}' created successfully.", userName);
        }
    }

    private static UserPreference DefaultPreferences(string lang) => new()
    {
        Theme               = "Light",
        Language            = lang,
        EmailNotifications  = true,
        ShowMetricsTrend    = true,
        DefaultPageSize     = 50,
    };
}
