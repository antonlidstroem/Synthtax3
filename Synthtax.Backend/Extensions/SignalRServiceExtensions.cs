using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synthtax.Backend.Hubs;
using Synthtax.Shared.SignalR;

namespace Synthtax.Backend.Extensions;

/// <summary>
/// Registrerar Fas 8 SignalR-infrastruktur i Program.cs.
///
/// <para><b>Anrop i Program.cs:</b>
/// <code>
///   // Efter AddSaasInfrastructure() från Fas 5
///   builder.Services.AddSynthtaxSignalR(builder.Configuration);
///
///   // I app-pipeline (efter UseAuthentication, UseAuthorization)
///   app.MapSynthtaxHubs();
/// </code>
/// </para>
/// </summary>
public static class SignalRServiceExtensions
{
    public static IServiceCollection AddSynthtaxSignalR(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        var signalRBuilder = services.AddSignalR(opts =>
        {
            // Klient-timeout: om ingen heartbeat ACK inom 60 s → disconnect
            opts.ClientTimeoutInterval    = TimeSpan.FromSeconds(60);
            opts.KeepAliveInterval        = TimeSpan.FromSeconds(15);
            opts.HandshakeTimeout         = TimeSpan.FromSeconds(15);
            opts.MaximumReceiveMessageSize = 64 * 1024; // 64 KB
            opts.EnableDetailedErrors      = false;      // true i Development
        });

        // Redis-backplane för horisontell skalning (aktiveras om redis-ConnectionString finns)
        var redisConn = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            signalRBuilder.AddStackExchangeRedis(redisConn, opts =>
            {
                opts.Configuration.ChannelPrefix =
                    Microsoft.Extensions.Caching.StackExchangeRedis.RedisChannel.Literal(
                        "synthtax-signalr");
            });
        }

        // JSON-serialisering för payloads (camelCase för JS-klienter, snake_case-kompatibel)
        signalRBuilder.AddJsonProtocol(opts =>
        {
            opts.PayloadSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
            opts.PayloadSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

        // Hub-pusher (Singleton — IHubContext är thread-safe)
        services.AddSingleton<ISynthtaxHubPusher, SynthtaxHubPusher>();

        // Heartbeat-bakgrundstjänst
        services.AddHostedService<HeartbeatHostedService>();

        return services;
    }

    /// <summary>Konfigurerar SignalR-hubens route i ASP.NET Core-pipelinen.</summary>
    public static WebApplication MapSynthtaxHubs(this WebApplication app)
    {
        app.MapHub<SynthtaxHub>("/hubs/synthtax", opts =>
        {
            // Tillåt WebSocket och Long Polling (fallback)
            opts.Transports =
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;

            // CORS hanteras av AddCors() i Program.cs
        });

        return app;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// HeartbeatHostedService  —  pushar Heartbeat var 30 s
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Bakgrundstjänst som pushar <see cref="HeartbeatPayload"/> var 30:e sekund
/// till alla anslutna klienter. VSIX-klienten använder detta för att
/// detektera att servern lever (komplement till SignalR:s inbyggda KeepAlive).
/// </summary>
internal sealed class HeartbeatHostedService : BackgroundService
{
    private readonly ISynthtaxHubPusher _pusher;
    private readonly ILogger<HeartbeatHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public HeartbeatHostedService(
        ISynthtaxHubPusher              pusher,
        ILogger<HeartbeatHostedService> logger)
    {
        _pusher = pusher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat-tjänst startad (intervall: {Sec} s).", Interval.TotalSeconds);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _pusher.PushHeartbeatAsync(new HeartbeatPayload
                {
                    ServerTime = DateTime.UtcNow
                }, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat-push misslyckades.");
            }
        }

        _logger.LogInformation("Heartbeat-tjänst stoppad.");
    }
}
