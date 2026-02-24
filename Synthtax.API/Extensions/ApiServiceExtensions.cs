using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Synthtax.Core.Interfaces;
using Synthtax.API.Services;

namespace Synthtax.API.Extensions;

public static class ApiServiceExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── JWT Settings ──────────────────────────────────────────────────
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
                          ?? throw new InvalidOperationException("JwtSettings not configured.");

        // ── JWT Authentication ────────────────────────────────────────────
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });

        // ── Authorization ─────────────────────────────────────────────────
        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            options.AddPolicy("UserOrAdmin", policy => policy.RequireRole("Admin", "User"));
        });

        // ── Application Services ──────────────────────────────────────────
        services.AddScoped<IJwtService, JwtService>();
        // Scoped so temp clone dirs are cleaned up after each request
        services.AddScoped<RepositoryResolverService>();

        // ── CORS ──────────────────────────────────────────────────────────
        services.AddCors(options =>
        {
            options.AddPolicy("SynthtaxPolicy", policy =>
            {
                policy
                    .AllowAnyOrigin()   // WPF-klient – justeras i produktion
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });

        // ── Controllers + JSON ────────────────────────────────────────────
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

        // ── Swagger ───────────────────────────────────────────────────────
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Synthtax API",
                Version = "v1",
                Description = "Code analysis and project intelligence platform"
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
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
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
