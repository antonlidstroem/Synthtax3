using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.Services;
using Synthtax.Vsix.SignalR;
using NSubstitute.Extensions;

namespace Synthtax.Vsix.Package;

/// <summary>
/// Fas 8-tillägg till <c>SynthtaxPackage</c> (Fas 7) via partial class.
///
/// <para><b>Tillägg i SynthtaxPackage.cs (Fas 7):</b>
/// <code>
///   // Lägg till partial keyword och de nya fälten:
///   public sealed partial class SynthtaxPackage : AsyncPackage
///   {
///       private ISynthtaxHubClient? _hubClient;
///       private StatusBarService?   _statusBarService;
///       private RealTimeUpdateService? _realTimeService;
///
///       public ISynthtaxHubClient HubClient =>
///           _hubClient ?? throw new InvalidOperationException("...");
///
///       // I InitializeAsync(), efter _promptDispatch = new PromptDispatchService():
///       await InitializeSignalRAsync(cancellationToken);
///   }
/// </code>
/// </para>
///
/// <para><b>Obs:</b> Eftersom Fas 7-filen inte kan modifieras här visas
/// den fullständiga integreringskoden som en standalone-klass med statisk
/// init-metod som anropas från <c>InitializeAsync</c>.</para>
/// </summary>
public static class SignalRPackageInitializer
{
    private static ISynthtaxHubClient?   _hubClient;
    private static StatusBarService?     _statusBarService;
    private static RealTimeUpdateService? _realTimeService;

    // Publika accessorer för övriga komponenter (CodeFix, ToolWindow)
    public static ISynthtaxHubClient?   HubClient    => _hubClient;
    public static RealTimeUpdateService? RealTimeService => _realTimeService;

    /// <summary>
    /// Anropas från <c>SynthtaxPackage.InitializeAsync()</c> som sista steg.
    ///
    /// <code>
    ///   // I SynthtaxPackage.InitializeAsync(), allra sist:
    ///   await SignalRPackageInitializer.InitializeAsync(this, cancellationToken);
    /// </code>
    /// </summary>
    public static async Task InitializeAsync(
        AsyncPackage package,
        CancellationToken ct)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        // ── Hämta AuthTokenService (skapades i Fas 7) ────────────────────
        // Hämta via SynthtaxPackage.Instance om det exponeras, annars via service
        var synthtaxPkg = (SynthtaxPackage?)Microsoft.VisualStudio.Shell.Package
            .GetGlobalService(typeof(SynthtaxPackage));

        if (synthtaxPkg is null) return;

        var auth = synthtaxPkg.AuthTokenService;

        // ── SignalR-klient ────────────────────────────────────────────────
        var logger = CreateLogger();
        _hubClient = new SynthtaxHubClient(auth, logger);

        // ── StatusBar ─────────────────────────────────────────────────────
        var vsStatusBar = await package.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;
        _statusBarService = new StatusBarService(_hubClient);
        if (vsStatusBar is not null)
            _statusBarService.Initialize(vsStatusBar);

        // ── RealTimeUpdateService ─────────────────────────────────────────
        _realTimeService = new RealTimeUpdateService(_hubClient, _statusBarService);

        // ── Koppla hub-statens ändringar till Tool Window ─────────────────
        _hubClient.ConnectionStateChanged += (_, state) =>
        {
            // Uppdatera ViewModel om Tool Window är öppet
        };

        // ── Starta anslutning om redan inloggad ───────────────────────────
        if (auth.IsAuthenticated)
        {
            // Starta bakgrundstråd — Connect blockerar ej UI
            _ = _hubClient.StartAsync(ct);
        }

        // ── Prenumerera på inloggningshändelse ───────────────────────────
        // (Auth-tjänsten har inget event API i Fas 7 — lägg till via polling
        //  eller utöka AuthTokenService med ett AuthenticationChanged-event)
    }

    /// <summary>
    /// Anropas av <c>LoginDialog</c> efter lyckad inloggning för att starta SignalR.
    /// </summary>
    public static async Task OnUserLoggedInAsync(CancellationToken ct = default)
    {
        if (_hubClient is null) return;

        if (_hubClient.State is HubConnectionState.Disconnected
                              or HubConnectionState.AuthError)
        {
            await _hubClient.StartAsync(ct);
        }
    }

    /// <summary>Anropas vid utloggning — stänger WebSocket.</summary>
    public static async Task OnUserLoggedOutAsync()
    {
        if (_hubClient is not null)
            await _hubClient.StopAsync();
    }

    /// <summary>Registrerar Tool Window som mottagare av realtidsuppdateringar.</summary>
    public static void RegisterToolWindow(IToolWindowRefreshTarget target)
        => _realTimeService?.RegisterToolWindow(target);

    /// <summary>Avregistrerar Tool Window (vid stängning).</summary>
    public static void UnregisterToolWindow()
        => _realTimeService?.UnregisterToolWindow();

    // Enkel logger-fabrik utan DI (VSIX har inget IoC-container per default)
    private static Microsoft.Extensions.Logging.ILogger<SynthtaxHubClient> CreateLogger()
    {
        var factory = Microsoft.Extensions.Logging.LoggerFactory.Create(lb =>
            lb.AddDebug().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
        return factory.CreateLogger<SynthtaxHubClient>();
    }
}
