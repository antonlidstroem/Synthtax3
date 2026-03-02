using Synthtax.Vsix.Diagnostics;
using Synthtax.Vsix.Services;

namespace Synthtax.Vsix.Package;

/// <summary>
/// Fas 8-tillägg till <c>SynthtaxPackage</c>.
///
/// <para>Integrera i <c>SynthtaxPackage.cs</c> enligt mönstret nedan.
/// Alla ändringar är markerade med <c>// FAS 8 ↓</c>.</para>
///
/// <para><b>Ändra i SynthtaxPackage:</b>
/// <code>
/// // Befintliga fält (Fas 7):
/// private SynthtaxApiClient?      _apiClient;
/// private AuthTokenService?       _authTokenService;
/// private PromptDispatchService?  _promptDispatch;
///
/// // FAS 8 ↓ — Lägg till:
/// private SynthtaxRealtimeService?  _realtimeService;
/// private StatusBarRealtimeService? _statusBarService;
/// private RealtimeDiagnosticBridge? _diagnosticBridge;
///
/// public SynthtaxRealtimeService RealtimeService =>
///     _realtimeService ?? throw new InvalidOperationException("Package not initialized");
/// public StatusBarRealtimeService StatusBarService =>
///     _statusBarService ?? throw new InvalidOperationException("Package not initialized");
///
/// // I InitializeAsync, efter befintlig init:
/// // FAS 8 ↓
/// _realtimeService  = new SynthtaxRealtimeService(
///     _authTokenService,
///     CreateLogger&lt;SynthtaxRealtimeService&gt;());
///
/// _statusBarService = new StatusBarRealtimeService(this);
///
/// _diagnosticBridge = new RealtimeDiagnosticBridge(this);
///
/// // Koppla statusfält till anslutningsstatus
/// _realtimeService.ConnectionStateChanged += _statusBarService.OnConnectionStateChanged;
///
/// // Koppla diagnostik-bryggan
/// _realtimeService.AnalysisUpdated += _diagnosticBridge.OnAnalysisUpdated;
/// _realtimeService.IssueCreated    += _diagnosticBridge.OnIssueCreated;
/// _realtimeService.IssueClosed     += _diagnosticBridge.OnIssueClosed;
///
/// // Auto-start om token finns
/// if (_authTokenService.IsAuthenticated)
///     _ = _realtimeService.StartAsync();
///
/// // I Dispose(bool disposing):
/// // FAS 8 ↓
/// _realtimeService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
/// _statusBarService?.Dispose();
/// </code>
/// </para>
/// </summary>
internal static class SynthtaxPackageFas8Guide
{
    // Denna fil är en guide-fil — ingen körbar kod.
    // Se kommentarerna ovan för exakt hur SynthtaxPackage.cs ska uppdateras.
}

/// <summary>
/// Extension-metod för att skapa en ILogger från ett AsyncPackage.
/// </summary>
internal static class AsyncPackageLoggerExtensions
{
    /// <summary>
    /// Skapar en <see cref="Microsoft.Extensions.Logging.ILogger"/> som
    /// skriver till VS Output-fönstret under "Synthtax"-kanalen.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger CreateLogger<T>(
        this Microsoft.VisualStudio.Shell.AsyncPackage package)
    {
        return new VsOutputWindowLogger(typeof(T).Name, package);
    }
}

/// <summary>
/// Minimal ILogger-implementation som skriver till VS Output Window.
/// </summary>
internal sealed class VsOutputWindowLogger : Microsoft.Extensions.Logging.ILogger
{
    private readonly string _categoryName;
    private readonly Microsoft.VisualStudio.Shell.AsyncPackage _package;

    // GUID för "Synthtax"-output-panel
    private static readonly Guid OutputPaneGuid = new("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    private Microsoft.VisualStudio.Shell.Interop.IVsOutputWindowPane? _pane;

    public VsOutputWindowLogger(string categoryName, Microsoft.VisualStudio.Shell.AsyncPackage package)
    {
        _categoryName = categoryName;
        _package      = package;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) =>
        logLevel >= Microsoft.Extensions.Logging.LogLevel.Debug;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = $"[Synthtax.{_categoryName}] [{logLevel}] {formatter(state, exception)}";
        if (exception is not null)
            message += $"\n  Exception: {exception.Message}";

        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var pane = EnsurePane();
            pane?.OutputStringThreadSafe(message + "\n");
        });
    }

    private Microsoft.VisualStudio.Shell.Interop.IVsOutputWindowPane? EnsurePane()
    {
        Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();

        if (_pane is not null) return _pane;

        var outputWindow = Microsoft.VisualStudio.Shell.Package
            .GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsOutputWindow))
            as Microsoft.VisualStudio.Shell.Interop.IVsOutputWindow;

        if (outputWindow is null) return null;

        var paneGuid = OutputPaneGuid;
        outputWindow.CreatePane(ref paneGuid, "Synthtax", 1, 1);
        outputWindow.GetPane(ref paneGuid, out _pane);
        return _pane;
    }
}
