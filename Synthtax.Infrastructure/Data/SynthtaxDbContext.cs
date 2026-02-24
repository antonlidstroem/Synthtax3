using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Data;

public class SynthtaxDbContext : IdentityDbContext<ApplicationUser>
{
    public SynthtaxDbContext(DbContextOptions<SynthtaxDbContext> options)
        : base(options)
    {
    }

    public DbSet<BacklogItem> BacklogItems => Set<BacklogItem>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── ApplicationUser ──────────────────────────────────────────────
        builder.Entity<ApplicationUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(200);
            e.Property(u => u.AllowedModules).HasMaxLength(2000);

            e.HasOne(u => u.Preferences)
             .WithOne(p => p.User)
             .HasForeignKey<UserPreference>(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(u => u.RefreshTokens)
             .WithOne(r => r.User)
             .HasForeignKey(r => r.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(u => u.BacklogItems)
             .WithOne(b => b.CreatedByUser)
             .HasForeignKey(b => b.CreatedByUserId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(u => u.AuditLogs)
             .WithOne(a => a.User)
             .HasForeignKey(a => a.UserId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ── RefreshToken ─────────────────────────────────────────────────
        builder.Entity<RefreshToken>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Token).HasMaxLength(512).IsRequired();
            e.HasIndex(r => r.Token).IsUnique();
            e.Property(r => r.ReplacedByToken).HasMaxLength(512);
            e.Property(r => r.CreatedByIp).HasMaxLength(50);
            e.Property(r => r.RevokedByIp).HasMaxLength(50);
        });

        // ── UserPreference ───────────────────────────────────────────────
        builder.Entity<UserPreference>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Theme).HasMaxLength(50).HasDefaultValue("Light");
            e.Property(p => p.Language).HasMaxLength(10).HasDefaultValue("sv-SE");
        });

        // ── BacklogItem ──────────────────────────────────────────────────
        builder.Entity<BacklogItem>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Title).HasMaxLength(500).IsRequired();
            e.Property(b => b.Description).HasMaxLength(4000);
            e.Property(b => b.Tags).HasMaxLength(500);
            e.Property(b => b.LinkedFilePath).HasMaxLength(1000);

            // Index för vanliga sökningar
            e.HasIndex(b => b.TenantId);
            e.HasIndex(b => b.CreatedByUserId);
            e.HasIndex(b => b.Status);
            e.HasIndex(b => b.Priority);
            e.HasIndex(b => new { b.TenantId, b.Status });
        });

        // ── AuditLog ─────────────────────────────────────────────────────
        builder.Entity<AuditLog>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Action).HasMaxLength(200).IsRequired();
            e.Property(a => a.ResourceType).HasMaxLength(100);
            e.Property(a => a.ResourceId).HasMaxLength(100);
            e.Property(a => a.Details).HasMaxLength(4000);
            e.Property(a => a.IpAddress).HasMaxLength(50);

            // Index för filtrering
            e.HasIndex(a => a.TenantId);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.Action);
            e.HasIndex(a => a.OccurredAt);
            e.HasIndex(a => new { a.TenantId, a.OccurredAt });
        });
    }
}
