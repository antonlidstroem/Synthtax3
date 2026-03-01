using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Synthtax.Core.Interfaces;
using Synthtax.API.Services;
using Synthtax.API.Middleware;

namespace Synthtax.API.Extensions;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
                          ?? throw new InvalidOperationException("JwtSettings not configured.");

        // SEC-08 FIX: Validera att JWT-nyckeln är tillräckligt lång.
        // HMAC-SHA256 kräver minst 32 bytes (256 bit) för säkerhet.
        if (jwtSettings.SecretKey.Length < 32)
            throw new InvalidOperationException(
                "JwtSettings:SecretKey must be at least 32 characters. " +
                "Set a strong key via environment variable JwtSettings__SecretKey.");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ValidateIssuer    = true,
                ValidIssuer       = jwtSettings.Issuer,
                ValidateAudience  = true,
                ValidAudience     = jwtSettings.Audience,
                ValidateLifetime  = true,
                ClockSkew         = TimeSpan.FromSeconds(30)
            };
        });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly",   policy => policy.RequireRole("Admin"));
            options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("Admin", "User"));
        });

        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<RepositoryResolverService>();
        services.AddScoped<IRepositoryResolver>(sp =>
            sp.GetRequiredService<RepositoryResolverService>());

        services.AddAuditWriter();  // registrerar AuditWriterBackgroundService
        

        // SEC-03 FIX: CORS tillåter inte längre alla origins (AllowAnyOrigin).
        // Origins läses från konfiguration (Cors:AllowedOrigins i appsettings / env-vars).
        services.AddCors(options =>
        {
            options.AddPolicy("SynthtaxPolicy", policy =>
            {
                var allowedOrigins = configuration
                    .GetSection("Cors:AllowedOrigins")
                    .Get<string[]>() ?? [];

                if (allowedOrigins.Length == 0)
                    throw new InvalidOperationException(
                        "Cors:AllowedOrigins is empty. " +
                        "Add at least one allowed origin in appsettings or environment variables.");

                policy
                    .WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        services.AddControllers()
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.Converters.Add(
                    new Newtonsoft.Json.Converters.StringEnumConverter());
                options.SerializerSettings.NullValueHandling =
                    Newtonsoft.Json.NullValueHandling.Ignore;
                options.SerializerSettings.DateTimeZoneHandling =
                    Newtonsoft.Json.DateTimeZoneHandling.Utc;
            });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title       = "Synthtax API",
                Version     = "v1",
                Description = "Code analysis and project intelligence platform"
            });
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name        = "Authorization",
                Type        = SecuritySchemeType.ApiKey,
                Scheme      = "Bearer",
                BearerFormat = "JWT",
                In          = ParameterLocation.Header,
                Description = "Enter: Bearer {token}"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id   = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
