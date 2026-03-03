using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synthtax.Application.Watchdog;
using Synthtax.Core.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

public sealed class WatchdogBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WatchdogBackgroundService> _logger;
    private readonly Dictionary<WatchdogSource, DateTimeOffset> _nextRun = new();

    public WatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<WatchdogBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("WatchdogBackgroundService started.");
        await RunAllCheckersAsync(forceRun: true, ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                await RunAllCheckersAsync(forceRun: false, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in WatchdogBackgroundService loop.");
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
        }

        _logger.LogInformation("WatchdogBackgroundService stopped.");
    }

    private async Task RunAllCheckersAsync(bool forceRun, CancellationToken ct)
    {
        using var scope    = _scopeFactory.CreateScope();
        var checkers       = scope.ServiceProvider.GetRequiredService<IEnumerable<IWatchdogSourceChecker>>();
        var db             = scope.ServiceProvider.GetRequiredService<SynthtaxDbContext>();
        var alertPublisher = scope.ServiceProvider.GetRequiredService<IAdminAlertPublisher>();

        foreach (var checker in checkers)
        {
            ct.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;

            if (!forceRun &&
                _nextRun.TryGetValue(checker.Source, out var nextRun) &&
                now < nextRun)
                continue;

            await RunSingleCheckerAsync(checker, db, alertPublisher, ct);
            _nextRun[checker.Source] = now + checker.CheckInterval;
        }
    }

    private async Task RunSingleCheckerAsync(
        IWatchdogSourceChecker checker,
        SynthtaxDbContext db,
        IAdminAlertPublisher alertPublisher,
        CancellationToken ct)
    {
        var sw  = Stopwatch.StartNew();
        var run = new WatchdogRun { Source = checker.Source, RanAt = DateTime.UtcNow };
        int newAlerts = 0;

        try
        {
            _logger.LogDebug("Watchdog running checker: {Source}", checker.Source);
            var findings = await checker.CheckAsync(ct);

            foreach (var finding in findings)
            {
                var created = await PersistFindingAsync(db, finding, ct);
                if (created is not null)
                {
                    newAlerts++;
                    await alertPublisher.PublishNewAlertAsync(created, ct);
                }
            }

            run.Success   = true;
            run.NewAlerts = newAlerts;

            if (newAlerts > 0)
                _logger.LogInformation(
                    "Watchdog {Source}: {Count} new alert(s) created.", checker.Source, newAlerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchdog checker {Source} failed.", checker.Source);
            run.Success      = false;
            run.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            run.DurationMs = (int)sw.ElapsedMilliseconds;
            db.WatchdogRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task<WatchdogAlert?> PersistFindingAsync(
        SynthtaxDbContext db,
        WatchdogFinding finding,
        CancellationToken ct)
    {
        var exists = await db.WatchdogAlerts
            .IgnoreQueryFilters()
            .AnyAsync(a =>
                a.Source             == finding.Source &&
                a.ExternalVersionKey == finding.ExternalVersionKey, ct);

        if (exists) return null;

        var alert = new WatchdogAlert
        {
            Id                  = Guid.NewGuid(),
            Source              = finding.Source,
            Severity            = finding.Severity,
            Status              = AlertStatus.New,
            ExternalVersionKey  = finding.ExternalVersionKey,
            Title               = finding.Title,
            Description         = finding.Description,
            ReleaseNotesUrl     = finding.ReleaseNotesUrl,
            ActionRequired      = finding.ActionRequired,
            ExternalPublishedAt = finding.ExternalPublishedAt,
            RawPayloadJson      = finding.RawPayloadJson
        };

        db.WatchdogAlerts.Add(alert);
        await db.SaveChangesAsync(ct);
        return alert;
    }
}
