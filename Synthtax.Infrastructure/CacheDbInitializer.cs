using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure;

/// <summary>
/// Ensures the SQLite analysis cache database is created and migrated on startup.
/// Also runs an initial cleanup of any stale data from previous runs.
/// </summary>
public static class CacheDbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILogger<AnalysisCacheDbContext>>();

        try
        {
            var cacheContext = scope.ServiceProvider
                .GetRequiredService<AnalysisCacheDbContext>();

            // EnsureCreated is intentional here – the cache DB uses no migrations,
            // just a simple auto-created schema. If the schema changes in a future
            // release, the file is deleted and recreated (data is temporary anyway).
            await cacheContext.Database.EnsureCreatedAsync();
            logger.LogInformation("Analysis cache database initialized.");

            // Clean up any sessions that expired during previous app runs
            var expired = await cacheContext.AnalysisSessions
                .Where(s => s.ExpiresAt <= DateTime.UtcNow)
                .ToListAsync();

            if (expired.Count > 0)
            {
                cacheContext.AnalysisSessions.RemoveRange(expired);
                await cacheContext.SaveChangesAsync();
                logger.LogInformation(
                    "Removed {Count} expired analysis sessions on startup.", expired.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize analysis cache database.");
            // Don't rethrow – cache failure should not prevent app startup
        }
    }
}
