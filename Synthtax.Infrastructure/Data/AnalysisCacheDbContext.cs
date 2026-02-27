using Microsoft.EntityFrameworkCore;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Data;

/// <summary>
/// Separate SQLite-backed DbContext for temporary analysis results.
/// Completely isolated from the main SQL Server database.
/// </summary>
public class AnalysisCacheDbContext : DbContext
{
    public AnalysisCacheDbContext(DbContextOptions<AnalysisCacheDbContext> options)
        : base(options)
    {
    }

    public DbSet<AnalysisSession> AnalysisSessions => Set<AnalysisSession>();
    public DbSet<SavedAnalysisIssue> AnalysisIssues => Set<SavedAnalysisIssue>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AnalysisSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.SolutionPath).HasMaxLength(2000).IsRequired();
            e.Property(s => s.SessionType).HasMaxLength(100).IsRequired();
            e.HasIndex(s => s.SolutionPath);
            e.HasIndex(s => s.ExpiresAt);
            e.HasIndex(s => s.CreatedAt);
            e.HasMany(s => s.Issues)
             .WithOne(i => i.Session)
             .HasForeignKey(i => i.SessionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SavedAnalysisIssue>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.FilePath).HasMaxLength(2000);
            e.Property(i => i.FileName).HasMaxLength(500);
            e.Property(i => i.IssueType).HasMaxLength(200);
            e.Property(i => i.Description).HasMaxLength(4000);
            // CodeSnippet and FixedCodeSnippet stored as TEXT (no max in SQLite)
            e.Property(i => i.MethodName).HasMaxLength(500);
            e.Property(i => i.ClassName).HasMaxLength(500);
            e.HasIndex(i => i.SessionId);
            e.HasIndex(i => i.IssueType);
            e.HasIndex(i => i.Severity);
            e.HasIndex(i => new { i.SessionId, i.IssueType });
        });
    }
}
