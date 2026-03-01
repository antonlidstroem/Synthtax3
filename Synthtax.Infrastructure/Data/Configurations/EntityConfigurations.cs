using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synthtax.Domain.Entities;

namespace Synthtax.Infrastructure.Data.Configurations;

// ═══════════════════════════════════════════════════════════════════════════
// RuleConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> b)
    {
        b.ToTable("Rules");

        // Naturlig nyckel — aldrig databas-genererad
        b.HasKey(r => r.RuleId);
        b.Property(r => r.RuleId)
         .HasMaxLength(20)
         .IsRequired()
         .ValueGeneratedNever();

        b.Property(r => r.Name)
         .HasMaxLength(200)
         .IsRequired();

        b.Property(r => r.Description)
         .HasMaxLength(2000);

        b.Property(r => r.Category)
         .HasMaxLength(100)
         .IsRequired();

        // Lagras som "High"/"Medium" etc. — läsbar vid direkta SQL-queries
        b.Property(r => r.DefaultSeverity)
         .HasConversion<string>()
         .HasMaxLength(20);

        b.Property(r => r.Version)
         .HasMaxLength(20)
         .HasDefaultValue("1.0.0");

        b.Property(r => r.IsEnabled)
         .HasDefaultValue(true);

        b.Property(r => r.CreatedAt).IsRequired();
        b.Property(r => r.CreatedBy).HasMaxLength(200);
        b.Property(r => r.LastModifiedAt);
        b.Property(r => r.LastModifiedBy).HasMaxLength(200);

        b.HasIndex(r => r.Category);
        b.HasIndex(r => r.IsEnabled);

        b.HasMany(r => r.BacklogItems)
         .WithOne(bi => bi.Rule)
         .HasForeignKey(bi => bi.RuleId)
         .OnDelete(DeleteBehavior.Restrict);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ProjectConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("Projects");

        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();

        b.Property(p => p.Name)
         .HasMaxLength(300)
         .IsRequired();

        b.Property(p => p.PhysicalPath).HasMaxLength(2000);
        b.Property(p => p.RemoteUrl).HasMaxLength(1000);

        b.Property(p => p.LanguageType)
         .HasConversion<string>()
         .HasMaxLength(30);

        b.Property(p => p.TierLevel)
         .HasConversion<int>(); // 1–4 i databasen

        b.Property(p => p.TenantId).IsRequired();

        // Optimistic Concurrency — SQL Server sätter automatiskt värdet vid varje UPDATE
        b.Property(p => p.RowVersion)
         .IsRowVersion()
         .IsConcurrencyToken();

        // Soft Delete
        b.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();
        b.Property(p => p.DeletedAt);
        b.Property(p => p.DeletedBy).HasMaxLength(200);

        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.CreatedBy).HasMaxLength(200);
        b.Property(p => p.LastModifiedAt);
        b.Property(p => p.LastModifiedBy).HasMaxLength(200);

        b.HasIndex(p => p.TenantId);
        b.HasIndex(p => new { p.TenantId, p.IsDeleted });
        b.HasIndex(p => p.TierLevel);

        b.HasMany(p => p.Sessions)
         .WithOne(s => s.Project)
         .HasForeignKey(s => s.ProjectId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(p => p.BacklogItems)
         .WithOne(bi => bi.Project)
         .HasForeignKey(bi => bi.ProjectId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// AnalysisSessionConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class AnalysisSessionConfiguration : IEntityTypeConfiguration<AnalysisSession>
{
    public void Configure(EntityTypeBuilder<AnalysisSession> b)
    {
        b.ToTable("AnalysisSessions");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();

        b.Property(s => s.ProjectId).IsRequired();
        b.Property(s => s.Timestamp).IsRequired();

        b.Property(s => s.OverallScore)
         .HasPrecision(5, 2); // Max 100.00

        b.Property(s => s.CommitSha)
         .HasMaxLength(40);

        b.Property(s => s.ErrorsJson)
         .HasColumnType("nvarchar(max)");

        b.Property(s => s.CreatedAt).IsRequired();
        b.Property(s => s.CreatedBy).HasMaxLength(200);
        b.Property(s => s.LastModifiedAt);
        b.Property(s => s.LastModifiedBy).HasMaxLength(200);

        // Vanligaste query: "senaste N sessioner för projekt X ordnat på tid"
        b.HasIndex(s => s.ProjectId);
        b.HasIndex(s => new { s.ProjectId, s.Timestamp });

        b.HasOne(s => s.Project)
         .WithMany(p => p.Sessions)
         .HasForeignKey(s => s.ProjectId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// BacklogItemConfiguration
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

        // SHA-256 hex = 64 tecken
        b.Property(bi => bi.Fingerprint)
         .HasMaxLength(64)
         .IsRequired();

        b.Property(bi => bi.Status)
         .HasConversion<string>()
         .HasMaxLength(30);

        b.Property(bi => bi.SeverityOverride)
         .HasConversion<string>()
         .HasMaxLength(20);

        b.Property(bi => bi.Metadata)
         .HasColumnType("nvarchar(max)");

        b.Property(bi => bi.TenantId).IsRequired();

        // Optimistic Concurrency
        b.Property(bi => bi.RowVersion)
         .IsRowVersion()
         .IsConcurrencyToken();

        // Soft Delete
        b.Property(bi => bi.IsDeleted).HasDefaultValue(false).IsRequired();
        b.Property(bi => bi.DeletedAt);
        b.Property(bi => bi.DeletedBy).HasMaxLength(200);

        b.Property(bi => bi.CreatedAt).IsRequired();
        b.Property(bi => bi.CreatedBy).HasMaxLength(200);
        b.Property(bi => bi.LastModifiedAt);
        b.Property(bi => bi.LastModifiedBy).HasMaxLength(200);

        // ── Index ──────────────────────────────────────────────────────────
        // Det kritiska unika indexet: förhindrar dubletter vid parallella analyser.
        // Filtreras på IsDeleted = false så att soft-deletade poster inte blockerar
        // återöppnande av en issue med samma fingerprint.
        b.HasIndex(bi => new { bi.ProjectId, bi.Fingerprint })
         .IsUnique()
         .HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UX_BacklogItems_Project_Fingerprint");

        b.HasIndex(bi => new { bi.ProjectId, bi.Status });
        b.HasIndex(bi => new { bi.TenantId, bi.Status });
        b.HasIndex(bi => bi.RuleId);

        // ── Relations ─────────────────────────────────────────────────────
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

        // EffectiveSeverity är en beräknad property — inte mappad till kolumn
        b.Ignore(bi => bi.EffectiveSeverity);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// CommentConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> b)
    {
        b.ToTable("Comments");

        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();

        b.Property(c => c.BacklogItemId).IsRequired();

        b.Property(c => c.Text)
         .HasMaxLength(8000)
         .IsRequired();

        b.Property(c => c.UserId)
         .HasMaxLength(450)  // ASP.NET Identity standard
         .IsRequired();

        b.Property(c => c.UserName)
         .HasMaxLength(256);

        b.Property(c => c.EditedAt);

        b.Property(c => c.CreatedAt).IsRequired();
        b.Property(c => c.CreatedBy).HasMaxLength(200);
        b.Property(c => c.LastModifiedAt);
        b.Property(c => c.LastModifiedBy).HasMaxLength(200);

        b.HasIndex(c => c.BacklogItemId);
        b.HasIndex(c => c.UserId);

        b.HasOne(c => c.BacklogItem)
         .WithMany(bi => bi.Comments)
         .HasForeignKey(c => c.BacklogItemId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
