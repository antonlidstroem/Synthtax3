using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Graph;
using Octokit;
using Synthtax.Core.Entities;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Services;
using Project = Synthtax.Core.Entities.Project;

namespace Synthtax.Infrastructure.Data;

public class SynthtaxDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentUserService? _currentUser;

    public SynthtaxDbContext(DbContextOptions<SynthtaxDbContext> options, ICurrentUserService? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // --- Core DbSets ---
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();
    public DbSet<BacklogItem> BacklogItems => Set<BacklogItem>();
    public DbSet<Comment> Comments => Set<Comment>();

    // --- Multi-tenancy & Identity DbSets ---
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();

    // --- Watchdog & Telemetry (Fas 9) ---
    public DbSet<WatchdogAlert> WatchdogAlerts => Set<WatchdogAlert>();
    public DbSet<PluginTelemetry> PluginTelemetry => Set<PluginTelemetry>();
    public DbSet<WatchdogRun> WatchdogRuns => Set<WatchdogRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Laddar alla konfigurationer (IEntityTypeConfiguration) från detta projekt
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SynthtaxDbContext).Assembly);

        // --- Global Query Filters ---
        // Vi kombinerar Soft Delete och Tenant-isolering i ett filter per entitet

        modelBuilder.Entity<Microsoft.CodeAnalysis.Project>().HasQueryFilter(p =>
            !p.IsDeleted && (_currentUser == null || _currentUser.IsSystemAdmin || p.TenantId == _currentUser.TenantId));

        modelBuilder.Entity<BacklogItem>().HasQueryFilter(bi =>
            !bi.IsDeleted && (_currentUser == null || _currentUser.IsSystemAdmin || bi.TenantId == _currentUser.TenantId));

        modelBuilder.Entity<Organization>().HasQueryFilter(o =>
            _currentUser == null || _currentUser.IsSystemAdmin || o.Id == _currentUser.OrganizationId);

        // Identity tabellnamn
        modelBuilder.Entity<ApplicationUser>().ToTable("AspNetUsers");
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ApplyAuditFields();
        return await base.SaveChangesAsync(ct);
    }

    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;
        var userId = _currentUser?.UserId ?? "system";

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy = userId;
            }

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified)
            {
                entry.Entity.LastModifiedAt = now;
                entry.Entity.LastModifiedBy = userId;
            }

            // Hantera Soft Delete
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable soft)
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAt = now;
                soft.DeletedBy = userId;
            }
        }
    }
}