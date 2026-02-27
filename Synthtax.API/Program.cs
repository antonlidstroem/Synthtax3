using Microsoft.AspNetCore.Identity;
using Microsoft.Build.Locator;
using Synthtax.API.Extensions;
using Synthtax.API.Middleware;
using Synthtax.API.Services.Analysis;
using Synthtax.Infrastructure;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Repositories;

if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// ─────────────────────────────────────────────────────────────────────────────
// CRITICAL ORDER: AddIdentity MUST be registered BEFORE AddApiServices.
//
// AddIdentity() internally calls AddAuthentication() and sets:
//   DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme  (cookie)
//   DefaultChallengeScheme    = IdentityConstants.ApplicationScheme  (cookie)
//
// AddApiServices() then calls AddAuthentication() again, which OVERRIDES
// those defaults with JwtBearerDefaults.AuthenticationScheme.
//
// If the order is reversed (Identity after JWT), Identity silently resets
// the defaults back to cookies — every API call returns 401 even with a
// valid Bearer token, because ASP.NET Core tries to authenticate via
// cookie instead of JWT. This was exactly the Swagger/401 bug.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 8;

    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<SynthtaxDbContext>()
.AddDefaultTokenProviders();

// JWT auth + CORS + Swagger — registered AFTER Identity so JWT wins as
// the default authentication/challenge scheme.
builder.Services.AddApiServices(builder.Configuration);

builder.Services.AddAnalysisServices(); // Roslyn + analysis engine

var app = builder.Build();

await Synthtax.Infrastructure.CacheDbInitializer.InitializeAsync(app.Services);

await DbSeeder.SeedAsync(app.Services);

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

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("SynthtaxPolicy");
app.UseAuditLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Timestamp = DateTime.UtcNow,
    Version = "1.0.0"
})).AllowAnonymous();

app.Run();
