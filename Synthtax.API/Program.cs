using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Build.Locator;
using Synthtax.API.Extensions;
using Synthtax.Application.Extensions;
using Synthtax.Core.Extensions;
using Synthtax.Infrastructure;
using Synthtax.Infrastructure.Data;


if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

// 1. Registrera tjänster (DI)
builder.Services.AddProblemDetails();
builder.Services.AddControllers().AddNewtonsoftJson();

// Infrastruktur & SaaS (Fas 5)
builder.Services.AddSynthtaxInfrastructure(builder.Configuration); // Innehåller DB & Repos
builder.Services.AddSaasInfrastructure(builder.Configuration);     // Innehåller SaaS-specifikt
builder.Services.AddSaasAuthentication(builder.Configuration);     // JWT & Policies

// Analys & Plugins
builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddAnalysisServices();
builder.Services.AddPluginCore();
builder.Services.AddOrchestrator();
builder.Services.AddFuzzyMatching();
builder.Services.AddSynthtaxSignalR(builder.Configuration);

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("analysis", o => { o.PermitLimit = 5; o.Window = TimeSpan.FromMinutes(1); });
    options.AddFixedWindowLimiter("auth", o => { o.PermitLimit = 10; o.Window = TimeSpan.FromMinutes(1); });
});

var app = builder.Build();

// 2. Configure Pipeline (Middleware)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "Synthtax API v1"); c.RoutePrefix = string.Empty; });
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseExceptionHandler(); // Använder AddProblemDetails automatiskt i .NET 8+

app.UseRouting();
app.UseCors("SynthtaxPolicy"); // Använder policyn definierad i AddApiServices
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseTenantContext();        // Fas 5: Måste ligga efter Auth

// Seedning & Init
await CacheDbInitializer.InitializeAsync(app.Services);
await DbSeeder.SeedAsync(app.Services);

// Endpoints
app.MapControllers();
app.MapSynthtaxHubs();
app.MapGet("/health", () => Results.Ok(new
{
    Status = "Healthy",
    Version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0"
})).AllowAnonymous();

app.Run();