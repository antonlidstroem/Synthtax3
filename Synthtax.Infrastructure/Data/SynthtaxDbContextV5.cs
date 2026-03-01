using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Synthtax.Domain.Entities;
using Synthtax.Infrastructure.Data.Configurations;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Services;

namespace Synthtax.Infrastructure.Data;

/// <summary>
/// Fas 5-uppdatering av <c>SynthtaxDbContext</c>.
///
/// <para><b>Ändringar från Fas 1:</b>
/// <list type="bullet">
///   <item>Global Query Filter kombinerar nu <b>soft-delete + tenant-isolering</b>
///         i ett enda lambda-uttryck per entitet (EF Core kräver detta).</item>
///   <item>Systemadmin (<c>IsSystemAdmin = true</c>) kringgår tenant-filtret —
///         ser all data för admin-ändamål.</item>
///   <item>Nya DbSets: Organizations, OrganizationMemberships, Invitations.</item>
/// </list>
/// </para>
///
/// <para><b>Query Filter-pattern:</b>
/// <code>
///   // Standard-anrop (tenant-isolerat):
///   _db.Projects.ToList()  →  WHERE IsDeleted=0 AND TenantId='org-guid'
///
///   // Admin-anrop (cross-tenant):
///   _db.Projects.IgnoreQueryFilters().ToList()  →  WHERE 1=1
/// </code>
/// </para>
///
/// <para><b>EF Core-begränsning:</b> Max ett <c>HasQueryFilter</c> per entitet.
/// Kombinera alltid soft-delete och tenant i ett och samma lambda.</para>
/// </summary>
public class SynthtaxDbContextV5 : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentUserService? _currentUser;

    // ── DbSets — Fas 1–3 ────────────────────────────────────────────────
    public DbSet<Rule>            Rules            => Set<Rule>();
    public DbSet<Project>         Projects         => Set<Project>();
    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();
    public DbSet<BacklogItem>     BacklogItems     => Set<BacklogItem>();
    public DbSet<Comment>         Comments         => Set<Comment>();

    // ── DbSets — Fas 5 ──────────────────────────────────────────────────
    public DbSet<Organization>         Organizations         => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<Invitation>           Invitations           => Set<Invitation>();

    public SynthtaxDbContextV5(
        DbContextOptions<SynthtaxDbContextV5> options,
        ICurrentUserService?                  currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    // ── Konfiguration ──────────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RuleConfiguration).Assembly);

        // ── Global Query Filters ──────────────────────────────────────────
        //
        // VIKTIGT: Dessa lambdas fångar 'this' — EF Core re-evaluerar dem
        // per query (inte vid konstruktion), så TenantId och IsSystemAdmin
        // läser korrekt värde för varje request.
        //
        // Mönster: !IsDeleted && (IsSystemAdmin || TenantId == currentTenantId)

        modelBuilder.Entity<Project>().HasQueryFilter(p =>
            !p.IsDeleted &&
            (_currentUser == null ||
             _currentUser.IsSystemAdmin ||
             p.TenantId == _currentUser.TenantId));

        modelBuilder.Entity<BacklogItem>().HasQueryFilter(bi =>
            !bi.IsDeleted &&
            (_currentUser == null ||
             _currentUser.IsSystemAdmin ||
             bi.TenantId == _currentUser.TenantId));

        // AnalysisSession har inget eget TenantId — filtreras via Project-relation
        // Men för direkta queries lägger vi ett projektbaserat filter:
        // (Skip om du alltid accessar Sessions via Project.Sessions navigation)

        // Organization — visas bara för egna organisationen (eller systemadmin)
        modelBuilder.Entity<Organization>().HasQueryFilter(o =>
            _currentUser == null ||
            _currentUser.IsSystemAdmin ||
            o.Id == _currentUser.OrganizationId);

        // OrganizationMembership — bara egna organisationens membrar
        modelBuilder.Entity<OrganizationMembership>().HasQueryFilter(m =>
            _currentUser == null ||
            _currentUser.IsSystemAdmin ||
            m.OrganizationId == _currentUser.OrganizationId);

        // Invitation — bara egna organisationens inbjudningar
        modelBuilder.Entity<Invitation>().HasQueryFilter(i =>
            _currentUser == null ||
            _currentUser.IsSystemAdmin ||
            i.OrganizationId == _currentUser.OrganizationId);

        // ── Identity-tabeller ─────────────────────────────────────────────
        modelBuilder.Entity<ApplicationUser>().ToTable("AspNetUsers");
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
        => base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => base.SaveChanges(acceptAllChangesOnSuccess);
}
