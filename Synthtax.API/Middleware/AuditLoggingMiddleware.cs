using System.Security.Claims;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Middleware;

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;
    // Hämtas en gång från root-provider – aldrig disposed under appens livstid
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/auth/logout",
        "/api/auth/register",
        "/api/admin/users",
        "/api/admin/reset-password",
        "/api/export"
    };

    public AuditLoggingMiddleware(
        RequestDelegate next,
        ILogger<AuditLoggingMiddleware> logger,
        IServiceScopeFactory scopeFactory)   // ← injiceras från root, aldrig disposed
    {
        _next = next;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        var statusCode = context.Response.StatusCode;

        bool shouldAudit =
            AuditedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || (method is "DELETE" && path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase));

        if (!shouldAudit) return;

        // Capture värden INNAN request-scopet stängs
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        var success = statusCode is >= 200 and < 400;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var action = DetermineAction(method, path);
        var resType = DetermineResourceType(path);
        var details = $"{method} {path} → {statusCode}";

        // Kör audit i bakgrunden med _scopeFactory (root-scope, aldrig disposed)
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

                await auditRepo.LogAsync(
                    userId: userId,
                    action: action,
                    resourceType: resType,
                    details: details,
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
