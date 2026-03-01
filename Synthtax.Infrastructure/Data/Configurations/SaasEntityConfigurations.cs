using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;

namespace Synthtax.Infrastructure.Data.Configurations;

// ═══════════════════════════════════════════════════════════════════════════
// OrganizationConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("Organizations");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();

        b.Property(o => o.Name)
         .HasMaxLength(200)
         .IsRequired();

        b.Property(o => o.Slug)
         .HasMaxLength(100)
         .IsRequired();

        b.HasIndex(o => o.Slug)
         .IsUnique()
         .HasDatabaseName("UX_Organizations_Slug");

        b.Property(o => o.Plan)
         .HasConversion<string>()
         .HasMaxLength(30)
         .IsRequired();

        b.Property(o => o.PurchasedLicenses)
         .HasDefaultValue(1)
         .IsRequired();

        b.Property(o => o.IsActive)
         .HasDefaultValue(true)
         .IsRequired();

        b.Property(o => o.BillingEmail)
         .HasMaxLength(254);

        // Audit
        b.Property(o => o.CreatedAt).IsRequired();
        b.Property(o => o.CreatedBy).HasMaxLength(200);
        b.Property(o => o.LastModifiedAt);
        b.Property(o => o.LastModifiedBy).HasMaxLength(200);

        // Ignorera beräknade properties
        b.Ignore(o => o.IsInTrial);
        b.Ignore(o => o.IsTrialExpired);
        b.Ignore(o => o.ActiveMemberCount);

        b.HasMany(o => o.Memberships)
         .WithOne(m => m.Organization)
         .HasForeignKey(m => m.OrganizationId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(o => o.Invitations)
         .WithOne(i => i.Organization)
         .HasForeignKey(i => i.OrganizationId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// OrganizationMembershipConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> b)
    {
        b.ToTable("OrganizationMemberships");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();

        b.Property(m => m.OrganizationId).IsRequired();

        b.Property(m => m.UserId)
         .HasMaxLength(450) // ASP.NET Identity standard
         .IsRequired();

        b.Property(m => m.Role)
         .HasConversion<string>()
         .HasMaxLength(20)
         .IsRequired();

        b.Property(m => m.IsActive)
         .HasDefaultValue(true)
         .IsRequired();

        // Unik constraint: en användare kan bara ha en aktiv membership per org
        b.HasIndex(m => new { m.OrganizationId, m.UserId })
         .IsUnique()
         .HasDatabaseName("UX_OrgMemberships_OrgUser");

        b.HasIndex(m => m.UserId);
        b.HasIndex(m => new { m.OrganizationId, m.IsActive });

        // Audit
        b.Property(m => m.CreatedAt).IsRequired();
        b.Property(m => m.CreatedBy).HasMaxLength(200);
        b.Property(m => m.LastModifiedAt);
        b.Property(m => m.LastModifiedBy).HasMaxLength(200);

        b.Ignore(m => m.IsOrgAdmin);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// InvitationConfiguration
// ═══════════════════════════════════════════════════════════════════════════

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("Invitations");
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();

        b.Property(i => i.OrganizationId).IsRequired();

        b.Property(i => i.Email)
         .HasMaxLength(254)
         .IsRequired();

        b.Property(i => i.TargetRole)
         .HasConversion<string>()
         .HasMaxLength(20)
         .IsRequired();

        b.Property(i => i.Token)
         .HasMaxLength(128)
         .IsRequired();

        b.HasIndex(i => i.Token)
         .IsUnique()
         .HasDatabaseName("UX_Invitations_Token");

        b.Property(i => i.Status)
         .HasConversion<string>()
         .HasMaxLength(20)
         .IsRequired();

        b.Property(i => i.AcceptedByUserId)
         .HasMaxLength(450);

        b.HasIndex(i => new { i.OrganizationId, i.Status });
        b.HasIndex(i => new { i.Email, i.OrganizationId });

        // Audit
        b.Property(i => i.CreatedAt).IsRequired();
        b.Property(i => i.CreatedBy).HasMaxLength(200);
        b.Property(i => i.LastModifiedAt);
        b.Property(i => i.LastModifiedBy).HasMaxLength(200);

        b.Ignore(i => i.IsExpired);
        b.Ignore(i => i.CanBeAccepted);
    }
}
