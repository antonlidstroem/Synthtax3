using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data.Configurations;
using Synthtax.Infrastructure.Entities; // ApplicationUser — befintlig Identity-entitet

namespace Synthtax.Infrastructure.Data;

/// <summary>
/// Huvud-DbContext för Synthtax.
///
/// <para><b>Global Query Filters</b> — automatiskt applicerade på alla queries:
/// <list type="bullet">
///   <item>Soft Delete: entiteter med <c>IsDeleted = true</c> är aldrig synliga.</item>
///   <item>Multi-tenancy: entiteter filtreras på <c>TenantId</c> (om inte system-anrop).</item>
/// </list>
/// Använd <c>.IgnoreQueryFilters()</c> explicit när du behöver hård-deletade eller
/// cross-tenant data (t.ex. i admin-endpoints).
/// </para>
///
/// <para><b>Optimistic Concurrency</b> — hanteras automatiskt av EF Core för
/// <c>Project</c> och <c>BacklogItem</c> via <c>RowVersion byte[]</c>.
/// Fånga <c>DbUpdateConcurrencyException</c> i service-lagret.</para>
/// </summary>
public class SynthtaxDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentUserService? _currentUser;

    // ── DbSet:ar ───────────────────────────────────────────────────────────
    public DbSet<Rule>            Rules            => Set<Rule>();
    public DbSet<Project>         Projects         => Set<Project>();
    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();
    public DbSet<BacklogItem>     BacklogItems     => Set<BacklogItem>();
    public DbSet<Comment>         Comments         => Set<Comment>();

    public DbSet<WatchdogAlert> WatchdogAlerts { get; set; }
    public DbSet<PluginTelemetry> PluginTelemetry { get; set; }
    public DbSet<WatchdogRun> WatchdogRuns { get; set; }

    public SynthtaxDbContext(
        DbContextOptions<SynthtaxDbContext> options,
        ICurrentUserService? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // ── Konfiguration ──────────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new WatchdogAlertConfiguration());
        modelBuilder.ApplyConfiguration(new PluginTelemetryConfiguration());
        modelBuilder.ApplyConfiguration(new WatchdogRunConfiguration());

        // Fluent API — hämtar alla IEntityTypeConfiguration<T> i assemblyt automatiskt
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RuleConfiguration).Assembly);

        // ── Global Query Filters ─────────────────────────────────────────
        // Notering: EF Core tillåter max ett filter per entitet via HasQueryFilter.
        // Om du behöver kombinera flera villkor gör du det i ett enda lambda-uttryck.

        // Soft Delete — filterar bort logiskt borttagna poster
        modelBuilder.Entity<Project>()
            .HasQueryFilter(p => !p.IsDeleted);

        modelBuilder.Entity<BacklogItem>()
            .HasQueryFilter(bi => !bi.IsDeleted);

        // Comment kaskaderas via BacklogItem — inget eget filter behövs,
        // men vi lägger ett för säkerhets skull om de queries direkt.
        // (Comments har ingen ISoftDeletable — hanteras via kaskad-delete.)

        // ── Tabell-prefix för Identity (valfritt) ─────────────────────────
        modelBuilder.Entity<ApplicationUser>().ToTable("AspNetUsers");
    }

    // ── SaveChangesAsync override ──────────────────────────────────────────

    /// <summary>
    /// Sätter audit-fält automatiskt innan sparning.
    /// Notera: Interceptorn <see cref="AuditSaveChangesInterceptor"/> hanterar
    /// detta numera. Denna override finns kvar som fallback och för tester
    /// utan full DI-stack.
    /// </summary>
    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void ApplyAuditFields()
    {
        var now  = DateTime.UtcNow;
        var user = _currentUser?.UserId ?? "system";

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Sätt bara om värdet inte redan är satt (interceptorn kan ha gjort det)
                    if (entry.Entity.CreatedAt == default)
                        entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy      ??= user;
                    entry.Entity.LastModifiedAt ??= now;
                    entry.Entity.LastModifiedBy ??= user;
                    break;

                case EntityState.Modified:
                    entry.Property(e => e.CreatedAt).IsModified  = false;
                    entry.Property(e => e.CreatedBy).IsModified  = false;
                    entry.Entity.LastModifiedAt = now;
                    entry.Entity.LastModifiedBy = user;
                    break;
            }

            // Soft Delete-konvertering (om interceptorn av någon anledning missats)
            if (entry.State == EntityState.Deleted &&
                entry.Entity is ISoftDeletable softDeletable)
            {
                entry.State = EntityState.Modified;
                softDeletable.IsDeleted = true;
                softDeletable.DeletedAt = now;
                softDeletable.DeletedBy = user;
                entry.Entity.LastModifiedAt = now;
                entry.Entity.LastModifiedBy = user;
            }
        }
    }
}
