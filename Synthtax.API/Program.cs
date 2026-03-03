using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Build.Locator;
using Synthtax.API;
using Synthtax.API.Extensions;
using Synthtax.API.Middleware;
using Synthtax.Application.Extensions;
using Synthtax.Core.Extensions;
using Synthtax.Infrastructure;
using Synthtax.Infrastructure.Data;

if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

// ── 1. Tjänster ───────────────────────────────────────────────────────────────

builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddNewtonsoftJson();

// Infrastruktur (DB, repos, JWT-auth)
builder.Services.AddSynthtaxInfrastructure(builder.Configuration);

// Analys & Plugins
builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddAnalysisServices();
builder.Services.AddPluginCore();
builder.Services.AddOrchestrator();
builder.Services.AddFuzzyMatching();

// SignalR (inbyggt i ASP.NET Core 9 — ingen extra NuGet-paket behövs)
builder.Services.AddSignalR();

// Super Admin / Watchdog (Fas 9)
builder.Services.AddSuperAdmin(builder.Configuration);

// Audit-loggning via bakgrundstjänst
builder.Services.AddAuditWriter();

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("analysis", o =>
    {
        o.PermitLimit = 5;
        o.Window      = TimeSpan.FromMinutes(1);
    });
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window      = TimeSpan.FromMinutes(1);
    });
});

var app = builder.Build();

// ── 2. Middleware-pipeline ────────────────────────────────────────────────────

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Synthtax API v1");
        c.RoutePrefix = string.Empty;
    });
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler();
app.UseRouting();
app.UseCors("SynthtaxPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAuditLogging();

// ── 3. Seedning & Init ────────────────────────────────────────────────────────

await CacheDbInitializer.InitializeAsync(app.Services);
await DbSeeder.SeedAsync(app.Services);

// ── 4. Endpoints ──────────────────────────────────────────────────────────────

app.MapControllers();
app.MapSuperAdminEndpoints();

app.MapGet("/health", () => Results.Ok(new
{
    Status  = "Healthy",
    Version = System.Reflection.Assembly.GetEntryAssembly()
                    ?.GetName().Version?.ToString() ?? "1.0.0"
})).AllowAnonymous();

app.Run();
