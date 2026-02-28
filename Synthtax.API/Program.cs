using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Build.Locator;
using Synthtax.API.Extensions;
using Synthtax.API.Middleware;
using Synthtax.Infrastructure;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;
using System.Threading.RateLimiting;

if (!MSBuildLocator.IsRegistered)
    MSBuildLocator.RegisterDefaults();

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure (EF Core, SQLite cache, repositories) ──────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── ASP.NET Identity ───────────────────────────────────────────────────────
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

// ── JWT, CORS, Swagger, controllers ───────────────────────────────────────
builder.Services.AddApiServices(builder.Configuration);

// ── Roslyn analysitjänster + bakgrundstjänster ────────────────────────────
builder.Services.AddAnalysisServices();

// ── Rate limiting (ASP.NET Core 7+) ───────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Tung analys: max 5 requests/minut per IP
    options.AddFixedWindowLimiter("analysis", o =>
    {
        o.PermitLimit             = 5;
        o.Window                  = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder    = QueueProcessingOrder.OldestFirst;
        o.QueueLimit              = 2;
    });

    // Auth-endpoints: max 10 requests/minut per IP (skyddar mot brute-force)
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit          = 10;
        o.Window               = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit           = 0;
    });
});

// ── Problem Details (RFC 7807) ─────────────────────────────────────────────
builder.Services.AddProblemDetails();

var app = builder.Build();

// ── Global exception handler ──────────────────────────────────────────────
// Returnerar RFC 7807 ProblemDetails istället för stack traces
app.UseExceptionHandler(appBuilder =>
{
    appBuilder.Run(async ctx =>
    {
        var exceptionFeature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex      = exceptionFeature?.Error;
        var logger  = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        if (ex is not null)
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                ctx.Request.Method, ctx.Request.Path);

        ctx.Response.StatusCode  = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = 500,
            Title  = "Internal Server Error",
            // Visa detaljer bara i Development
            Detail = app.Environment.IsDevelopment() ? ex?.Message : null,
            Instance = ctx.Request.Path
        };

        await ctx.Response.WriteAsJsonAsync(problem);
    });
});

// ── Database init & seed ───────────────────────────────────────────────────
await Synthtax.Infrastructure.CacheDbInitializer.InitializeAsync(app.Services);
await DbSeeder.SeedAsync(app.Services);

// ── Swagger ────────────────────────────────────────────────────────────────
// Aktiverat i alla miljöer för testbarhet – begränsa åtkomst i produktion
// via nätverksregler eller autentisering framför /swagger-routen.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Synthtax API v1");
    c.RoutePrefix = string.Empty;
});

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("SynthtaxPolicy");
app.UseRateLimiter();
app.UseAuditLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    Status    = "Healthy",
    Timestamp = DateTime.UtcNow,
    Version   = "1.0.0"
})).AllowAnonymous();

app.Run();
