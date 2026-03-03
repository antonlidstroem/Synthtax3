using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Synthtax.Core.Entities;

namespace Synthtax.Infrastructure.Data.Configurations;

public class WatchdogAlertConfiguration : IEntityTypeConfiguration<WatchdogAlert>
{
    public void Configure(EntityTypeBuilder<WatchdogAlert> b)
    {
        b.ToTable("WatchdogAlerts");
        b.HasKey(a => a.Id);
        b.Property(a => a.Source).HasConversion<int>();
        b.Property(a => a.ExternalVersionKey).HasMaxLength(200).IsRequired();
        b.HasIndex(a => new { a.Source, a.ExternalVersionKey }).IsUnique();
    }
}