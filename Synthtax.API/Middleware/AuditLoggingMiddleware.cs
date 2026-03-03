using System.Security.Claims;
using System.Threading.Channels;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Middleware;

// ─── Datatyp för köade poster ────────────────────────────────────────────────
// CS0052-fix: AuditEntry måste vara public eftersom AuditChannel.Instance är public.

public sealed record AuditEntry(
    string  UserId,
    string  Action,
    string  ResourceType,
    string  Details,
    string? IpAddress,
    bool    Success);

// ─── Kanalfabrik ──────────────────────────────────────────────────────────────

public static class AuditChannel
{
    public static readonly Channel<AuditEntry> Instance =
        Channel.CreateBounded<AuditEntry>(new BoundedChannelOptions(1000)
        {
            FullMode      = BoundedChannelFullMode.Wait,
            SingleWriter  = false,
            SingleReader  = true
        });
}

// ─── Middleware ───────────────────────────────────────────────────────────────

public class AuditLoggingMiddleware
{
    private readonly RequestDelegate                _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    private static readonly HashSet<string> AuditedPaths =
        new(StringComparer.OrdinalIgnoreCase)
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
        ILogger<AuditLoggingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var path   = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;
        var status = context.Response.StatusCode;

        bool shouldAudit =
            AuditedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            || (method is "DELETE" && path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase));

        if (!shouldAudit) return;

        var entry = new AuditEntry(
            UserId:       context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            Action:       DetermineAction(method, path),
            ResourceType: DetermineResourceType(path),
            Details:      $"{method} {path} → {status}",
            IpAddress:    context.Connection.RemoteIpAddress?.ToString(),
            Success:      status is >= 200 and < 400);

        if (!AuditChannel.Instance.Writer.TryWrite(entry))
            _logger.LogWarning("Audit channel full — dropped audit entry for {Action}", entry.Action);
    }

    private static string DetermineAction(string method, string path)
    {
        if (path.Contains("/login",          StringComparison.OrdinalIgnoreCase)) return "Login";
        if (path.Contains("/logout",         StringComparison.OrdinalIgnoreCase)) return "Logout";
        if (path.Contains("/register",       StringComparison.OrdinalIgnoreCase)) return "Register";
        if (path.Contains("/reset-password", StringComparison.OrdinalIgnoreCase)) return "ResetPassword";
        if (path.Contains("/export",         StringComparison.OrdinalIgnoreCase)) return "Export";
        if (path.Contains("/admin/users",    StringComparison.OrdinalIgnoreCase))
            return method switch
            {
                "POST"            => "CreateUser",
                "DELETE"          => "DeleteUser",
                "PUT" or "PATCH"  => "UpdateUser",
                _                 => "ViewUsers"
            };
        return $"{method}:{path}";
    }

    private static string DetermineResourceType(string path)
    {
        if (path.Contains("/users",  StringComparison.OrdinalIgnoreCase)) return "User";
        if (path.Contains("/export", StringComparison.OrdinalIgnoreCase)) return "Export";
        if (path.Contains("/auth",   StringComparison.OrdinalIgnoreCase)) return "Auth";
        return "API";
    }
}

// ─── Background Service ───────────────────────────────────────────────────────

public sealed class AuditWriterBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory                 _scopeFactory;
    private readonly ILogger<AuditWriterBackgroundService> _logger;

    public AuditWriterBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditWriterBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditWriterBackgroundService started.");
        var reader = AuditChannel.Instance.Reader;

        while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false)
               || !reader.Completion.IsCompleted)
        {
            while (reader.TryRead(out var entry))
                await WriteEntryAsync(entry, stoppingToken);
        }

        _logger.LogInformation("AuditWriterBackgroundService stopped.");
    }

    private async Task WriteEntryAsync(AuditEntry entry, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await auditRepo.LogAsync(
                userId:       entry.UserId,
                action:       entry.Action,
                resourceType: entry.ResourceType,
                details:      entry.Details,
                ipAddress:    entry.IpAddress,
                success:      entry.Success);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for action {Action}", entry.Action);
        }
    }
}

// ─── Extension methods ────────────────────────────────────────────────────────

public static class AuditLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder app)
        => app.UseMiddleware<AuditLoggingMiddleware>();

    public static IServiceCollection AddAuditWriter(this IServiceCollection services)
    {
        services.AddHostedService<AuditWriterBackgroundService>();
        return services;
    }
}
