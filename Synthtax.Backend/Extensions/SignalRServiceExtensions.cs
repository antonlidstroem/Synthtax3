using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;                  // BUGFIX #6: rätt namespace för RedisChannel
using Synthtax.Application.Services;
using Synthtax.Backend.Hubs;
using Synthtax.Realtime.Contracts;

namespace Synthtax.Backend.Extensions;

public static class SignalRServiceExtensions
{
    public static IServiceCollection AddSynthtaxSignalR(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        var signalRBuilder = services.AddSignalR(opts =>
        {
            opts.ClientTimeoutInterval     = TimeSpan.FromSeconds(60);
            opts.KeepAliveInterval         = TimeSpan.FromSeconds(15);
            opts.HandshakeTimeout          = TimeSpan.FromSeconds(15);
            opts.MaximumReceiveMessageSize = 64 * 1024;
            opts.EnableDetailedErrors      = false;
        });

        var redisConn = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConn))
        {
            signalRBuilder.AddStackExchangeRedis(redisConn, opts =>
            {
                // BUGFIX #6: Microsoft.Extensions.Caching.StackExchangeRedis.RedisChannel
                // är fel typ — den tillhör caching-paketet, inte SignalR Redis-paketet.
                // Rätt är StackExchange.Redis.RedisChannel.Literal (från StackExchange.Redis).
                opts.Configuration.ChannelPrefix =
                    RedisChannel.Literal("synthtax-signalr");
            });
        }

        signalRBuilder.AddJsonProtocol(opts =>
        {
            opts.PayloadSerializerOptions.PropertyNamingPolicy =
                System.Text.Json.JsonNamingPolicy.CamelCase;
            opts.PayloadSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

        // Registrera den konkreta implementationen en gång …
        services.AddSingleton<SynthtaxHubPusher>();
        // … exponera via båda interfacen mot samma instans.
        services.AddSingleton<ISynthtaxHubPusher>(sp =>
            sp.GetRequiredService<SynthtaxHubPusher>());
        services.AddSingleton<IHubPusher>(sp =>
            sp.GetRequiredService<SynthtaxHubPusher>());

        services.AddHostedService<HeartbeatHostedService>();

        return services;
    }

    public static WebApplication MapSynthtaxHubs(this WebApplication app)
    {
        app.MapHub<SynthtaxHub>("/hubs/synthtax", opts =>
        {
            opts.Transports =
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
        });
        return app;
    }
}

internal sealed class HeartbeatHostedService : BackgroundService
{
    private readonly ISynthtaxHubPusher              _pusher;
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
        _logger.LogInformation(
            "Heartbeat-tjänst startad (intervall: {Sec} s).", Interval.TotalSeconds);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _pusher.PushHeartbeatAsync(
                    new HeartbeatEvent { ServerTime = DateTime.UtcNow },
                    stoppingToken);
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
