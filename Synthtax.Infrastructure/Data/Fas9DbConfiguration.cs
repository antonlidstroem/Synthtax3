using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synthtax.Application.SuperAdmin;
using Synthtax.Application.Watchdog;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Services;

namespace Synthtax.Infrastructure.Data;

// ═══════════════════════════════════════════════════════════════════════════
// DbContext-tillägg (partial class — kompileras ihop med SynthtaxDbContext)
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Partial DbContext-utvidgning för Fas 9-entiteter.
///
/// <para>Lägg till följande i <c>SynthtaxDbContext.cs</c>:
/// <code>
/// // Fas 9 ↓
/// public DbSet&lt;WatchdogAlert&gt;  WatchdogAlerts  { get; set; } = null!;
/// public DbSet&lt;PluginTelemetry&gt; PluginTelemetry { get; set; } = null!;
/// public DbSet&lt;WatchdogRun&gt;    WatchdogRuns    { get; set; } = null!;
///
/// // I OnModelCreating:
/// modelBuilder.ApplyConfiguration(new WatchdogAlertConfig());
/// modelBuilder.ApplyConfiguration(new PluginTelemetryConfig());
/// modelBuilder.ApplyConfiguration(new WatchdogRunConfig());
/// </code>
/// </para>
///
/// <para>Migration:
/// <code>
/// dotnet ef migrations add Fas9_WatchdogAndTelemetry --project Synthtax.Infrastructure
/// dotnet ef database update
/// </code>
/// </para>
/// </summary>
internal static class Fas9DbContextGuide { }

// ═══════════════════════════════════════════════════════════════════════════
// EF Core-konfigurationer
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>EF Core entity-konfiguration för <see cref="WatchdogAlert"/>.</summary>
public sealed class WatchdogAlertConfig : IEntityTypeConfiguration<WatchdogAlert>
{
    public void Configure(EntityTypeBuilder<WatchdogAlert> b)
    {
        b.ToTable("WatchdogAlerts");
        b.HasKey(a => a.Id);

        b.Property(a => a.Source).HasConversion<int>();
        b.Property(a => a.Severity).HasConversion<int>();
        b.Property(a => a.Status).HasConversion<int>();

        b.Property(a => a.ExternalVersionKey).HasMaxLength(200).IsRequired();
        b.Property(a => a.Title).HasMaxLength(300).IsRequired();
        b.Property(a => a.Description).HasMaxLength(2000);
        b.Property(a => a.ReleaseNotesUrl).HasMaxLength(500);
        b.Property(a => a.ActionRequired).HasMaxLength(1000);
        b.Property(a => a.AcknowledgedBy).HasMaxLength(100);
        b.Property(a => a.ResolvedBy).HasMaxLength(100);
        b.Property(a => a.RawPayloadJson).HasColumnType("nvarchar(max)");

        // Unikt index: ingen dubblettlarm per källa + version
        b.HasIndex(a => new { a.Source, a.ExternalVersionKey })
            .IsUnique()
            .HasDatabaseName("IX_WatchdogAlerts_Source_ExternalVersionKey");

        b.HasIndex(a => a.Status);
        b.HasIndex(a => a.Severity);

        // Tenant-filter kringgås alltid för WatchdogAlerts (är inte tenant-specifika)
        // Ingen HasQueryFilter här — de är globala system-entiteter
    }
}

/// <summary>EF Core entity-konfiguration för <see cref="PluginTelemetry"/>.</summary>
public sealed class PluginTelemetryConfig : IEntityTypeConfiguration<PluginTelemetry>
{
    public void Configure(EntityTypeBuilder<PluginTelemetry> b)
    {
        b.ToTable("PluginTelemetry");
        b.HasKey(t => t.Id);

        b.Property(t => t.PluginVersion).HasMaxLength(30).IsRequired();
        b.Property(t => t.VsVersionBucket).HasMaxLength(10).IsRequired();
        b.Property(t => t.OsPlatform).HasMaxLength(30).IsRequired();

        // Unik per installation + period (deduplicering)
        b.HasIndex(t => new { t.InstallationId, t.PeriodStart, t.PeriodEnd })
            .IsUnique()
            .HasDatabaseName("IX_PluginTelemetry_Installation_Period");

        b.HasIndex(t => t.PeriodStart);
        b.HasIndex(t => t.PluginVersion);

        // Numeriska kolumner med precision
        b.Property(t => t.MedianApiLatencyMs).HasPrecision(10, 2);
        b.Property(t => t.P95ApiLatencyMs).HasPrecision(10, 2);
        b.Property(t => t.SignalRUptimeFraction).HasPrecision(5, 4);
    }
}

/// <summary>EF Core entity-konfiguration för <see cref="WatchdogRun"/>.</summary>
public sealed class WatchdogRunConfig : IEntityTypeConfiguration<WatchdogRun>
{
    public void Configure(EntityTypeBuilder<WatchdogRun> b)
    {
        b.ToTable("WatchdogRuns");
        b.HasKey(r => r.Id);

        b.Property(r => r.Source).HasConversion<int>();
        b.Property(r => r.ErrorMessage).HasMaxLength(1000);

        b.HasIndex(r => r.Source);
        b.HasIndex(r => r.RanAt);

        // Retention: automatisk rensning av gamla körningsloggar (90 dagar)
        // Hanteras av TelemetryRetentionJob
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DI-registrering
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// DI-extension för Fas 9. Anropas i <c>Program.cs</c>:
/// <code>builder.Services.AddSynthtaxSuperAdmin(builder.Configuration);</code>
/// </summary>
public static class SuperAdminServiceExtensions
{
    public static IServiceCollection AddSynthtaxSuperAdmin(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        // ── Application-services ──────────────────────────────────────────
        services.AddScoped<IOrgAdminService,   OrgAdminService>();
        services.AddScoped<IAlertService,      AlertService>();
        services.AddScoped<ITelemetryService,  TelemetryService>();

        // ── Hub publisher ─────────────────────────────────────────────────
        services.AddScoped<IAdminAlertPublisher, AdminAlertHubPublisher>();

        // ── Watchdog-checkers ─────────────────────────────────────────────
        services.AddHttpClient("WatchdogClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Synthtax-Watchdog/1.0");
        });

        services.AddTransient<IWatchdogSourceChecker, VsReleaseChecker>();
        services.AddTransient<IWatchdogSourceChecker, AiModelChangelogChecker>();
        services.AddTransient<IWatchdogSourceChecker, NuGetPackageChecker>();
        services.AddTransient<IWatchdogSourceChecker, RoslynSdkChecker>();

        // ── Bakgrundstjänst ───────────────────────────────────────────────
        services.AddSingleton<WatchdogBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<WatchdogBackgroundService>());

        // ── Retention-bakgrundsjobb ───────────────────────────────────────
        services.AddHostedService<TelemetryRetentionJob>();

        // ── SignalR (AdminHub) ─────────────────────────────────────────────
        // Obs: AddSignalR() anropas i Program.cs:
        //   app.MapHub<AdminHub>("/hubs/admin");
        //
        // Authorization-policy:
        //   builder.Services.AddAuthorization(opts =>
        //       opts.AddPolicy("SystemAdmin", p =>
        //           p.RequireClaim("synthtax:is_system_admin", "true")));

        return services;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// TelemetryRetentionJob  — nattlig rensning av gammal telemetridata
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bakgrundstjänst som rensar telemetridata äldre än 90 dagar.
/// Körs kl. 03:00 UTC varje natt.
/// </summary>
internal sealed class TelemetryRetentionJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryRetentionJob> _logger;

    public TelemetryRetentionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryRetentionJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Beräkna delay till nästa 03:00 UTC
            var now     = DateTime.UtcNow;
            var nextRun = now.Date.AddDays(now.Hour >= 3 ? 1 : 0).AddHours(3);
            var delay   = nextRun - now;

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            using var scope     = _scopeFactory.CreateScope();
            var telemetry       = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

            try
            {
                var deleted = await telemetry.PurgeOldDataAsync(retentionDays: 90, ct);
                _logger.LogInformation(
                    "TelemetryRetentionJob: purged {Count} old records.", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TelemetryRetentionJob failed.");
            }
        }
    }
}
