using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Synthtax.Infrastructure.Data;

public class SynthtaxDbContextFactory : IDesignTimeDbContextFactory<SynthtaxDbContext>
{
    public SynthtaxDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SynthtaxDbContext>();

        // Peka på API-projektets appsettings.json
        var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "Synthtax.API");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(projectDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        optionsBuilder.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsAssembly(typeof(SynthtaxDbContextFactory).Assembly.FullName);
        });

        return new SynthtaxDbContext(optionsBuilder.Options);
    }
}