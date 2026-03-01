using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Build.Locator;
using Synthtax.API.Extensions;
using Synthtax.API.Middleware;
using Synthtax.Application.Extensions;
using Synthtax.Core.Extensions;
using Synthtax.Infrastructure;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;
using Synthtax.Infrastructure.Extensions;


if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit           = true;
    options.Password.RequireLowercase       = true;
    options.Password.RequireUppercase       = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength         = 8;
    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers      = true;
    options.User.RequireUniqueEmail         = true;
    options.SignIn.RequireConfirmedEmail     = false;
})
.AddEntityFrameworkStores<SynthtaxDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddApiServices(builder.Configuration);
builder.Services.AddAnalysisServices();



builder.Services.AddPluginCore();
builder.Services.AddOrchestrator();
builder.Services.AddFuzzyMatching();

builder.Services.AddSaasInfrastructure(builder.Configuration);
builder.Services.AddSaasAuthentication(builder.Configuration);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Analys-endpoints: 5 requests/minut med kö
    options.AddFixedWindowLimiter("analysis", o =>
    {
        o.PermitLimit          = 5;
        o.Window               = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit           = 2;
    });

    // Auth-endpoints: 10 requests/minut utan kö (brute-force skydd)
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit          = 10;
        o.Window               = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit           = 0;
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler(appBuilder =>
{
    appBuilder.Run(async ctx =>
    {
        var exceptionFeature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex     = exceptionFeature?.Error;
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        if (ex is not null)
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);

        ctx.Response.StatusCode  = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        {
            Status   = 500,
            Title    = "Internal Server Error",
            Detail   = app.Environment.IsDevelopment() ? ex?.Message : null,
            Instance = ctx.Request.Path
        };
        await ctx.Response.WriteAsJsonAsync(problem);
    });
});

await Synthtax.Infrastructure.CacheDbInitializer.InitializeAsync(app.Services);
await DbSeeder.SeedAsync(app.Services);

// SEC-04 FIX: Swagger exponeras nu ENBART i Development.
// Tidigare låg UseSwagger() utanför IsDevelopment-blocket.
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

app.UseCors("SynthtaxPolicy");
app.UseRateLimiter();
app.UseAuditLogging();
app.UseAuthentication();
app.UseSaasTenantContext();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    Status    = "Healthy",
    Timestamp = DateTime.UtcNow,
    // ARCH-05 FIX: Dynamisk version istället för hårdkodad "1.0.0"
    Version   = System.Reflection.Assembly.GetEntryAssembly()
                    ?.GetName().Version?.ToString() ?? "1.0.0"
})).AllowAnonymous();

// SEC-05 FIX: Rate limiting är nu faktiskt APPLICERAT.
// [EnableRateLimiting("analysis")] läggs i AnalysisController, CodeController,
// SecurityController, PipelineController, MetricsController, CouplingController.
// [EnableRateLimiting("auth")] läggs i AuthController.
// Se respektive controller-fix.

app.Run();
