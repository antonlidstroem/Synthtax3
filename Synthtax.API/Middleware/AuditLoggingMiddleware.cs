using System.Security.Claims;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Middleware;

/// <summary>
/// Middleware som automatiskt loggar utvalda API-anrop i audit-loggen.
/// </summary>
public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    // Endpoints som alltid ska auditloggas
    private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/register",
        "/api/admin/users",
        "/api/admin/reset-password",
        "/api/export"
    };

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        // Logga bara anrop mot utvalda paths och POST/DELETE
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        var statusCode = context.Response.StatusCode;

        bool shouldAudit = AuditedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        || (method is "DELETE" && path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase));

        if (!shouldAudit) return;

        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var success = statusCode is >= 200 and < 400;
        var ip = context.Connection.RemoteIpAddress?.ToString();

        // Gör audit-loggning asynkront i bakgrunden för att inte påverka svarstid
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = context.RequestServices.CreateScope();
                var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

                var action = DetermineAction(method, path);
                await auditRepo.LogAsync(
                    userId: userId,
                    action: action,
                    resourceType: DetermineResourceType(path),
                    details: $"{method} {path} → {statusCode}",
                    ipAddress: ip,
                    success: success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log for {Method} {Path}", method, path);
            }
        });
    }

    private static string DetermineAction(string method, string path)
    {
        if (path.Contains("/login", StringComparison.OrdinalIgnoreCase)) return "Login";
        if (path.Contains("/logout", StringComparison.OrdinalIgnoreCase)) return "Logout";
        if (path.Contains("/register", StringComparison.OrdinalIgnoreCase)) return "Register";
        if (path.Contains("/reset-password", StringComparison.OrdinalIgnoreCase)) return "ResetPassword";
        if (path.Contains("/export", StringComparison.OrdinalIgnoreCase)) return "Export";
        if (path.Contains("/admin/users", StringComparison.OrdinalIgnoreCase))
            return method switch
            {
                "POST" => "CreateUser",
                "DELETE" => "DeleteUser",
                "PUT" or "PATCH" => "UpdateUser",
                _ => "ViewUsers"
            };

        return $"{method}:{path}";
    }

    private static string DetermineResourceType(string path)
    {
        if (path.Contains("/users", StringComparison.OrdinalIgnoreCase)) return "User";
        if (path.Contains("/export", StringComparison.OrdinalIgnoreCase)) return "Export";
        if (path.Contains("/auth", StringComparison.OrdinalIgnoreCase)) return "Auth";
        return "API";
    }
}

public static class AuditLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder app)
        => app.UseMiddleware<AuditLoggingMiddleware>();
}
