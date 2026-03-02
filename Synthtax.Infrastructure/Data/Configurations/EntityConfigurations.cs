using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synthtax.Domain.Entities; // VIKTIGT: Använd domän-entiteterna

namespace Synthtax.Infrastructure.Data.Configurations;

// ═══════════════════════════════════════════════════════════════════════════
// BacklogItemConfiguration (Fas 1-4 Sammanslagen)
// ═══════════════════════════════════════════════════════════════════════════
public class BacklogItemConfiguration : IEntityTypeConfiguration<BacklogItem>
{
    public void Configure(EntityTypeBuilder<BacklogItem> b)
    {
        b.ToTable("BacklogItems");

        b.HasKey(bi => bi.Id);
        b.Property(bi => bi.Id).ValueGeneratedNever();
        b.Property(bi => bi.ProjectId).IsRequired();

        b.Property(bi => bi.RuleId)
         .HasMaxLength(20)
         .IsRequired();

        // FAS 4: Fingerprint & Historik
        b.Property(bi => bi.Fingerprint)
         .HasMaxLength(64)
         .IsRequired();

        b.Property(bi => bi.PreviousFingerprints)
         .HasColumnType("nvarchar(max)");

        b.Property(bi => bi.Status)
         .HasConversion<string>()
         .HasMaxLength(30);

        b.Property(bi => bi.SeverityOverride)
         .HasConversion<string>()
         .HasMaxLength(20);

        b.Property(bi => bi.Metadata)
         .HasColumnType("nvarchar(max)");

        b.Property(bi => bi.LastSeenInSessionId);
        b.Property(bi => bi.TenantId).IsRequired();

        // FAS 3: AutoClosed-flaggor
        b.Property(bi => bi.AutoClosed)
         .HasDefaultValue(false)
         .IsRequired();

        b.Property(bi => bi.AutoClosedInSessionId);
        b.Property(bi => bi.ReopenedInSessionId);

        // Optimistic Concurrency
        b.Property(bi => bi.RowVersion)
         .IsRowVersion()
         .IsConcurrencyToken();

        // Soft Delete
        b.Property(bi => bi.IsDeleted).HasDefaultValue(false).IsRequired();
        b.Property(bi => bi.DeletedAt);
        b.Property(bi => bi.DeletedBy).HasMaxLength(200);

        // Audit
        b.Property(bi => bi.CreatedAt).IsRequired();
        b.Property(bi => bi.CreatedBy).HasMaxLength(200);
        b.Property(bi => bi.LastModifiedAt);
        b.Property(bi => bi.LastModifiedBy).HasMaxLength(200);

        // ── Index ──────────────────────────────────────────────────────────

        // Unikt index (Fas 4): Tillåter återanvändning av fingerprint om den gamla är soft-deletad
        b.HasIndex(bi => new { bi.ProjectId, bi.Fingerprint })
         .IsUnique()
         .HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UX_BacklogItems_Project_Fingerprint");

        // Optimerade index för queries
        b.HasIndex(bi => new { bi.ProjectId, bi.Status });
        b.HasIndex(bi => new { bi.ProjectId, bi.AutoClosed, bi.Status });
        b.HasIndex(bi => new { bi.TenantId, bi.Status });
        b.HasIndex(bi => bi.RuleId);

        // ── Relationer ─────────────────────────────────────────────────────
        b.HasOne(bi => bi.Project)
         .WithMany(p => p.BacklogItems)
         .HasForeignKey(bi => bi.ProjectId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(bi => bi.Rule)
         .WithMany(r => r.BacklogItems)
         .HasForeignKey(bi => bi.RuleId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(bi => bi.Comments)
         .WithOne(c => c.BacklogItem)
         .HasForeignKey(c => c.BacklogItemId)
         .OnDelete(DeleteBehavior.Cascade);

        b.Ignore(bi => bi.EffectiveSeverity);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Project, Rule, AnalysisSession, Comment (Samma som förut)
// ═══════════════════════════════════════════════════════════════════════════

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> b)
    {
        b.ToTable("Rules");
        b.HasKey(r => r.RuleId);
        b.Property(r => r.RuleId).HasMaxLength(20).IsRequired().ValueGeneratedNever();
        b.Property(r => r.Name).HasMaxLength(200).IsRequired();
        b.Property(r => r.Category).HasMaxLength(100).IsRequired();
        b.Property(r => r.DefaultSeverity).HasConversion<string>().HasMaxLength(20);
        b.Property(r => r.IsEnabled).HasDefaultValue(true);
    }
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("Projects");
        b.HasKey(p => p.Id);
        b.Property(p => p.Name).HasMaxLength(300).IsRequired();
        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.RowVersion).IsRowVersion();
        b.Property(p => p.IsDeleted).HasDefaultValue(false);
    }
}

public class AnalysisSessionConfiguration : IEntityTypeConfiguration<AnalysisSession>
{
    public void Configure(EntityTypeBuilder<AnalysisSession> b)
    {
        b.ToTable("AnalysisSessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.OverallScore).HasPrecision(5, 2);
        b.HasIndex(s => new { s.ProjectId, s.Timestamp });
    }
}

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> b)
    {
        b.ToTable("Comments");
        b.HasKey(c => c.Id);
        b.Property(c => c.Text).HasMaxLength(8000).IsRequired();
        b.HasIndex(c => c.BacklogItemId);
    }
}