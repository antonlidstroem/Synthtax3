using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Synthtax.Application.Telemetry;

/// <summary>
/// Bakgrundstjänst som dagligen rensar telemetriposter äldre än 90 dagar.
///
/// <para>Körs med ett slumpmässigt offset (0–60 min) efter midnatt UTC
/// för att undvika belastningspikar om flera instanser körs parallellt.</para>
/// </summary>
public sealed class TelemetryPurgeBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory         _scopes;
    private readonly ILogger<TelemetryPurgeBackgroundService> _logger;

    private const int RetentionDays = 90;

    public TelemetryPurgeBackgroundService(
        IServiceScopeFactory scopes,
        ILogger<TelemetryPurgeBackgroundService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Slumpmässig start-delay för att sprida ut last
        var jitter = TimeSpan.FromMinutes(Random.Shared.Next(0, 60));
        await Task.Delay(jitter, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunPurgeAsync(stoppingToken);
            await WaitUntilTomorrowMidnightAsync(stoppingToken);
        }
    }

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        try
        {
            await using var scope   = _scopes.CreateAsyncScope();
            var healthSvc = scope.ServiceProvider
                .GetRequiredService<IGlobalHealthService>();

            await healthSvc.PurgeOldRecordsAsync(RetentionDays, ct);
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telemetry purge job failed.");
        }
    }

    private static async Task WaitUntilTomorrowMidnightAsync(CancellationToken ct)
    {
        var now       = DateTime.UtcNow;
        var tomorrow  = now.Date.AddDays(1);
        var delay     = tomorrow - now;
        await Task.Delay(delay, ct);
    }
}
