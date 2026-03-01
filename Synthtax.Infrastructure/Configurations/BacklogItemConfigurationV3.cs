using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synthtax.Domain.Entities;

namespace Synthtax.Infrastructure.Data.Configurations;

/// <summary>
/// Fas 3-uppdatering av BacklogItemConfiguration.
/// Ersätter definitionen i EntityConfigurations.cs.
///
/// Nytt: <c>AutoClosed</c>, <c>AutoClosedInSessionId</c>, <c>ReopenedInSessionId</c>.
/// Dessa läggs även till i BacklogItem-entiteten i Entities.cs.
/// </summary>
public class BacklogItemConfigurationV3 : IEntityTypeConfiguration<BacklogItem>
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

        b.Property(bi => bi.LastSeenInSessionId);
        b.Property(bi => bi.TenantId).IsRequired();

        // ── FAS 3: AutoClosed-flaggor ─────────────────────────────────────
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

        // Unikt index — filtrerat på IsDeleted=0, möjliggör återanvändning av fingerprint
        b.HasIndex(bi => new { bi.ProjectId, bi.Fingerprint })
         .IsUnique()
         .HasFilter("[IsDeleted] = 0")
         .HasDatabaseName("UX_BacklogItems_Project_Fingerprint");

        // Optimerade index för orchestrator-queries
        b.HasIndex(bi => new { bi.ProjectId, bi.Status });
        b.HasIndex(bi => new { bi.ProjectId, bi.AutoClosed, bi.Status });
        b.HasIndex(bi => new { bi.TenantId, bi.Status });
        b.HasIndex(bi => bi.RuleId);

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
