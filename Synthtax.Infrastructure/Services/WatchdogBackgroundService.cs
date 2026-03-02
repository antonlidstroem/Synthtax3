using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synthtax.Application.SuperAdmin.DTOs;
using Synthtax.Application.Watchdog;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Infrastructure.Services;

/// <summary>
/// Bakgrundstjänst som periodiskt kör alla <see cref="IWatchdogSourceChecker"/>-instanser,
/// persiserar fynd som <see cref="WatchdogAlert"/>-entiteter och skickar push-notiser
/// via SignalR till super-admin-dashboarden.
///
/// <para><b>Schema:</b>
/// Varje checker har sitt eget <see cref="IWatchdogSourceChecker.CheckInterval"/>.
/// Tjänsten håller en timer per källa och kör dem oberoende av varandra.</para>
///
/// <para><b>Idempotens:</b>
/// <c>ExternalVersionKey</c> + <c>Source</c> är unikt index i DB.
/// Befintliga alerts skapas inte om.</para>
///
/// <para><b>Felhantering:</b>
/// En checker som kastar exception stänger inte ner tjänsten —
/// felet loggas och körningen registreras som misslyckad i <see cref="WatchdogRun"/>.</para>
/// </summary>
public sealed class WatchdogBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WatchdogBackgroundService> _logger;

    // Håller track på nästa körning per källa
    private readonly Dictionary<WatchdogSource, DateTimeOffset> _nextRun = new();

    public WatchdogBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<WatchdogBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Huvud-loop
    // ═══════════════════════════════════════════════════════════════════════

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("WatchdogBackgroundService started.");

        // Kör alla checkers en gång vid uppstart för att fylla initial-data
        await RunAllCheckersAsync(forceRun: true, ct);

        // Huvud-loop: vaknar varje minut och kollar om någon checker är dags
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
                await Task.Delay(TimeSpan.FromMinutes(5), ct); // Back-off
            }
        }

        _logger.LogInformation("WatchdogBackgroundService stopped.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Körning
    // ═══════════════════════════════════════════════════════════════════════

    private async Task RunAllCheckersAsync(bool forceRun, CancellationToken ct)
    {
        using var scope     = _scopeFactory.CreateScope();
        var checkers        = scope.ServiceProvider
            .GetRequiredService<IEnumerable<IWatchdogSourceChecker>>();
        var db              = scope.ServiceProvider.GetRequiredService<SynthtaxDbContext>();
        var alertPublisher  = scope.ServiceProvider.GetRequiredService<IAdminAlertPublisher>();

        foreach (var checker in checkers)
        {
            ct.ThrowIfCancellationRequested();

            var now = DateTimeOffset.UtcNow;

            if (!forceRun &&
                _nextRun.TryGetValue(checker.Source, out var nextRun) &&
                now < nextRun)
                continue;  // Inte dags än

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
        var sw   = Stopwatch.StartNew();
        var run  = new WatchdogRun { Source = checker.Source, RanAt = DateTime.UtcNow };
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

            // Spara körningslogg (IgnoreQueryFilters — system-kontext)
            db.WatchdogRuns.Add(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Persistering + idempotens
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sparar ett <see cref="WatchdogFinding"/> som ett <see cref="WatchdogAlert"/>.
    /// Returnerar null om larmet redan finns (idempotent).
    /// </summary>
    private static async Task<WatchdogAlert?> PersistFindingAsync(
        SynthtaxDbContext db,
        WatchdogFinding finding,
        CancellationToken ct)
    {
        // Idempotens-check: samma Source + ExternalVersionKey → skippa
        var exists = await db.WatchdogAlerts
            .IgnoreQueryFilters()
            .AnyAsync(a =>
                a.Source              == finding.Source &&
                a.ExternalVersionKey  == finding.ExternalVersionKey,
                ct);

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

// ═══════════════════════════════════════════════════════════════════════════
// IAdminAlertPublisher  — push-notis till super-admin-dashboard via SignalR
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Skickar realtidsnotiser om nya watchdog-alerts till super-admin via SignalR.
/// Registreras som Scoped i DI.
/// </summary>
public interface IAdminAlertPublisher
{
    Task PublishNewAlertAsync(WatchdogAlert alert, CancellationToken ct = default);
}

/// <summary>
/// IHubContext-baserad implementation. Pushar till den fasta gruppen
/// <c>"superadmin"</c> — alla inloggade super-admins prenumererar dit.
/// </summary>
public sealed class AdminAlertHubPublisher : IAdminAlertPublisher
{
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<AdminHub> _hub;
    private readonly ILogger<AdminAlertHubPublisher> _logger;

    // Hub-metodnamn
    public const string NewAlertMethod     = "NewWatchdogAlert";
    public const string AlertUpdatedMethod = "WatchdogAlertUpdated";
    public const string AdminGroupName     = "superadmin";

    public AdminAlertHubPublisher(
        Microsoft.AspNetCore.SignalR.IHubContext<AdminHub> hub,
        ILogger<AdminAlertHubPublisher> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    public async Task PublishNewAlertAsync(WatchdogAlert alert, CancellationToken ct = default)
    {
        var payload = new
        {
            id                  = alert.Id,
            source              = alert.Source.ToString(),
            severity            = alert.Severity.ToString(),
            title               = alert.Title,
            description         = alert.Description,
            externalVersionKey  = alert.ExternalVersionKey,
            releaseNotesUrl     = alert.ReleaseNotesUrl,
            actionRequired      = alert.ActionRequired,
            externalPublishedAt = alert.ExternalPublishedAt,
            createdAt           = alert.CreatedAt
        };

        await _hub.Clients.Group(AdminGroupName)
            .SendAsync(NewAlertMethod, payload, ct);

        _logger.LogInformation(
            "Admin alert pushed: [{Severity}] {Title}", alert.Severity, alert.Title);
    }
}
