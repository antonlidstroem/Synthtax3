using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Background;

/// <summary>
/// Bakgrundstjänst som periodiskt rensar utgångna analyssessioner från SQLite-cachen.
/// Körs automatiskt utan att kräva manuellt anrop till /api/analysisresults/sessions/cleanup.
/// 
/// Registreras i Program.cs:
///   builder.Services.AddHostedService&lt;CacheCleanupBackgroundService&gt;();
/// </summary>
public sealed class CacheCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CacheCleanupBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public CacheCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CacheCleanupBackgroundService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;

        // Konfigurerbart intervall, default 1 timme.
        // Lägg till i appsettings.json: "CacheCleanup": { "IntervalMinutes": 60 }
        var minutes = configuration.GetValue<int>("CacheCleanup:IntervalMinutes", defaultValue: 60);
        _interval   = TimeSpan.FromMinutes(Math.Max(5, minutes)); // Minimum 5 min
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CacheCleanupBackgroundService started. Cleanup interval: {Interval}",
            _interval);

        // Fördröj första körningen 2 minuter efter start för att inte störa uppstart
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunCleanupAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("CacheCleanupBackgroundService stopping.");
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cacheService = scope.ServiceProvider.GetRequiredService<IAnalysisCacheService>();

            var removed = await cacheService.CleanupExpiredSessionsAsync(cancellationToken);

            if (removed > 0)
                _logger.LogInformation(
                    "Cache cleanup removed {Count} expired analysis session(s).", removed);
            else
                _logger.LogDebug("Cache cleanup: no expired sessions found.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown – ignorera
        }
        catch (Exception ex)
        {
            // Logga men krascha inte tjänsten
            _logger.LogError(ex, "Error during scheduled cache cleanup.");
        }
    }
}
