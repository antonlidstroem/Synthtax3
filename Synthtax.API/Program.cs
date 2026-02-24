using Microsoft.AspNetCore.Identity;
using Microsoft.Build.Locator;
using Synthtax.API.Extensions;
using Synthtax.API.Middleware;
using Synthtax.API.Services.Analysis;
using Synthtax.Infrastructure;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Repositories;

// ── MSBuild Locator (must be called before any Roslyn workspace usage) ────────
// Register the default MSBuild instance so MSBuildWorkspace can load solutions.
if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddAnalysisServices(); // Roslyn + analysis engine

// UserRepository is Infrastructure-specific, register here
//builder.Services.AddScoped<UserRepository>();


builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Lösenordspolicy
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

    // Låsningspolicy
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // Användarpolicy
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
        .AddEntityFrameworkStores<SynthtaxDbContext>()
        .AddDefaultTokenProviders();

// ── Build App ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Seed Database ─────────────────────────────────────────────────────────────
await DbSeeder.SeedAsync(app.Services);

// ── Middleware Pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Synthtax API v1");
        c.RoutePrefix = string.Empty; // Swagger på root
    });
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
//app.UseCors("SynthtaxPolicy");

// Bara HTTPS-omdirigering i produktion, inte i utveckling
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("SynthtaxPolicy");

// Audit logging middleware – körs innan auth för att fånga misslyckade försök
app.UseAuditLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Version = "1.0.0"
})).AllowAnonymous();

app.Run();
