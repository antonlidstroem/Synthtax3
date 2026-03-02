using System.Collections.Concurrent;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Synthtax.Vsix.Services;

/// <summary>
/// Samlar in och skickar anonymiserad hälsotelemetri från VSIX-pluginet.
///
/// <para><b>Datainsamling (in-memory):</b>
/// <list type="bullet">
///   <item>API-svarstider (median + P95 beräknas ur cirkulär buffer).</item>
///   <item>Antal misslyckade API-anrop.</item>
///   <item>Antal Roslyn-analyzer-crashar (utan stacktrace).</item>
///   <item>SignalR uptime-fraktion.</item>
/// </list>
/// </para>
///
/// <para><b>Rapportering:</b>
/// Skickas en gång per timme till <c>/api/v1/telemetry</c>.
/// Misslyckad rapportering hanteras tyst — telemetri är icke-kritisk.</para>
///
/// <para><b>Privacy:</b>
/// <list type="bullet">
///   <item>InstallationId = slumpmässigt GUID, sparat i IsolatedStorage.</item>
///   <item>VS-version rundas till major.minor ("17.10").</item>
///   <item>Ingen stacktrace, ingen filsökväg, ingen användardata.</item>
/// </list>
/// </para>
/// </summary>
public sealed class VsixTelemetryReporter : IDisposable
{
    private readonly Client.SynthtaxApiClient _api;
    private readonly Auth.AuthTokenService    _auth;
    private readonly Timer                    _reportTimer;

    // Cirklärbuffer för API-latenser (senaste 500 mätningar)
    private readonly ConcurrentQueue<double> _latencyBuffer = new();
    private const int MaxLatencySamples = 500;

    // Räknare
    private int _totalRequests;
    private int _failedRequests;
    private int _analyzerCrashes;

    // SignalR-uptime-tracking
    private DateTime? _connectedSince;
    private TimeSpan  _totalConnectedTime = TimeSpan.Zero;
    private readonly DateTime _periodStart;

    // Singleton InstallationId (anonymt, persisted i AppData)
    private static readonly Guid InstallationId = LoadOrCreateInstallationId();

    public VsixTelemetryReporter(
        Client.SynthtaxApiClient api,
        Auth.AuthTokenService auth)
    {
        _api         = api;
        _auth        = auth;
        _periodStart = DateTime.UtcNow;

        // Rapportera en gång per timme
        _reportTimer = new Timer(TimeSpan.FromHours(1).TotalMilliseconds) { AutoReset = true };
        _reportTimer.Elapsed += async (_, _) => await ReportAsync();
        _reportTimer.Start();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Insamlingsmetoder (anropas av SynthtaxApiClient och RealtimeService)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Registrera ett lyckats API-anrop med svarstid i ms.</summary>
    public void RecordSuccess(double latencyMs)
    {
        Interlocked.Increment(ref _totalRequests);
        EnqueueLatency(latencyMs);
    }

    /// <summary>Registrera ett misslyckat API-anrop.</summary>
    public void RecordFailure(double latencyMs = 0)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _failedRequests);
        if (latencyMs > 0) EnqueueLatency(latencyMs);
    }

    /// <summary>Registrera en Roslyn-analyzer-crash (utan detaljer).</summary>
    public void RecordAnalyzerCrash() =>
        Interlocked.Increment(ref _analyzerCrashes);

    /// <summary>SignalR ansluten — starta uptime-mätning.</summary>
    public void RecordSignalRConnected() =>
        _connectedSince = DateTime.UtcNow;

    /// <summary>SignalR bortkopplad — ackumulera uptime.</summary>
    public void RecordSignalRDisconnected()
    {
        if (_connectedSince.HasValue)
        {
            _totalConnectedTime += DateTime.UtcNow - _connectedSince.Value;
            _connectedSince = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Rapportering
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Skicka telemetri till backend. Tyst vid fel.</summary>
    public async Task ReportAsync()
    {
        if (!_auth.IsAuthenticated) return; // Inget JWT → skippa

        try
        {
            var now     = DateTime.UtcNow;
            var latencies = _latencyBuffer.ToArray();
            var (median, p95) = CalculatePercentiles(latencies);
            var uptimeFraction = CalculateSignalRUptime(now);

            var request = new Client.TelemetryIngestRequest
            {
                InstallationId       = InstallationId,
                PluginVersion        = GetPluginVersion(),
                VsVersionBucket      = GetVsVersionBucket(),
                OsPlatform           = GetOsPlatform(),
                MedianApiLatencyMs   = median,
                P95ApiLatencyMs      = p95,
                FailedRequestCount   = _failedRequests,
                TotalRequestCount    = _totalRequests,
                AnalyzerCrashCount   = _analyzerCrashes,
                SignalRUptimeFraction = uptimeFraction,
                PeriodStart          = _periodStart,
                PeriodEnd            = now
            };

            await _api.PostTelemetryAsync(request);

            // Nollställ räknare efter lyckad rapportering
            ResetCounters();
        }
        catch
        {
            // Tyst — telemetri är icke-kritisk och ska aldrig störa användaren
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Beräkningar
    // ═══════════════════════════════════════════════════════════════════════

    private static (double median, double p95) CalculatePercentiles(double[] values)
    {
        if (values.Length == 0) return (0, 0);
        var sorted = values.OrderBy(v => v).ToArray();
        var median = sorted[sorted.Length / 2];
        var p95Idx = (int)Math.Floor(sorted.Length * 0.95);
        var p95    = sorted[Math.Min(p95Idx, sorted.Length - 1)];
        return (Math.Round(median, 1), Math.Round(p95, 1));
    }

    private double CalculateSignalRUptime(DateTime now)
    {
        var periodDuration = now - _periodStart;
        if (periodDuration.TotalSeconds < 1) return 0;

        var connected = _totalConnectedTime;
        if (_connectedSince.HasValue)
            connected += now - _connectedSince.Value;

        return Math.Clamp(connected.TotalSeconds / periodDuration.TotalSeconds, 0, 1);
    }

    private void EnqueueLatency(double ms)
    {
        _latencyBuffer.Enqueue(ms);
        // Trimma buffern
        while (_latencyBuffer.Count > MaxLatencySamples)
            _latencyBuffer.TryDequeue(out _);
    }

    private void ResetCounters()
    {
        Interlocked.Exchange(ref _totalRequests,   0);
        Interlocked.Exchange(ref _failedRequests,  0);
        Interlocked.Exchange(ref _analyzerCrashes, 0);
        while (_latencyBuffer.TryDequeue(out _)) { }
        _totalConnectedTime = TimeSpan.Zero;
        _connectedSince     = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Platform-info
    // ═══════════════════════════════════════════════════════════════════════

    private static string GetPluginVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "0.0.0";

    private static string GetVsVersionBucket()
    {
        try
        {
            var dte = Microsoft.VisualStudio.Shell.Package
                .GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var ver = dte?.Version ?? "17.0";
            var parts = ver.Split('.');
            return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : ver;
        }
        catch { return "unknown"; }
    }

    private static string GetOsPlatform()
    {
        var ver = System.Environment.OSVersion.Version;
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
        {
            return ver.Build >= 22000 ? "Windows 11" : "Windows 10";
        }
        return "Other";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // InstallationId persistering
    // ═══════════════════════════════════════════════════════════════════════

    private static Guid LoadOrCreateInstallationId()
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Synthtax", "installation-id.txt");

        try
        {
            if (System.IO.File.Exists(path) &&
                Guid.TryParse(System.IO.File.ReadAllText(path).Trim(), out var existing))
                return existing;

            var newId = Guid.NewGuid();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, newId.ToString("D"));
            return newId;
        }
        catch { return Guid.NewGuid(); } // Fallback: icke-persistent men okej
    }

    public void Dispose()
    {
        _reportTimer.Dispose();
        // Sista rapport vid dispose (fire-and-forget)
        _ = ReportAsync();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// SynthtaxApiClient-tillägg för telemetri-endpoint
// ═══════════════════════════════════════════════════════════════════════════

// Lägg till följande metod i SynthtaxApiClient (Fas 7):
//
// public async Task PostTelemetryAsync(TelemetryIngestRequest request, CancellationToken ct = default)
// {
//     var client = GetAuthenticatedClient();
//     var resp   = await client.PostAsJsonAsync("api/v1/telemetry", request, ct);
//     // Tyst vid fel — telemetri är icke-kritisk
// }

namespace Synthtax.Vsix.Client
{
    /// <summary>Telemetri-DTO som VSIX-klienten skickar till backend.</summary>
    public sealed class TelemetryIngestRequest
    {
        public required Guid   InstallationId       { get; init; }
        public required string PluginVersion         { get; init; }
        public required string VsVersionBucket       { get; init; }
        public required string OsPlatform            { get; init; }
        public required double MedianApiLatencyMs    { get; init; }
        public required double P95ApiLatencyMs       { get; init; }
        public required int    FailedRequestCount    { get; init; }
        public required int    TotalRequestCount     { get; init; }
        public required int    AnalyzerCrashCount    { get; init; }
        public required double SignalRUptimeFraction  { get; init; }
        public required DateTime PeriodStart          { get; init; }
        public required DateTime PeriodEnd            { get; init; }
    }
}
