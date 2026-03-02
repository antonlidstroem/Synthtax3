using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.Services;

/// <summary>
/// Hanterar Synthtax-statusindikatorn i Visual Studios statusfält.
///
/// <para><b>Design:</b> Diskret — visar bara Synthtax-status när det är
/// relevant (anslutningsändringar, analysresultat). Återgår automatiskt
/// till standardläge efter konfigurerad tid.</para>
///
/// <para><b>Format i statusfältet:</b>
/// <code>
///   ● Synthtax: Ansluten          (Connected — grön punkt)
///   ◌ Synthtax: Ansluter…         (Connecting — spinner)
///   ○ Synthtax: Frånkopplad       (Disconnected — grå ring)
///   ⚠ Synthtax: Återsansluter…   (Reconnecting — varning)
///   🔑 Synthtax: Logga in         (AuthError)
/// </code>
/// </para>
/// </summary>
public sealed class StatusBarService : IDisposable
{
    private readonly ISynthtaxHubClient _hub;

    // VS statusbar — null om tjänsten ej är tillgänglig
    private IVsStatusbar? _statusBar;
    private uint          _animationCookie;
    private bool          _animationRunning;
    private string        _lastConnectionText = "";

    // Timer för auto-restore
    private System.Threading.Timer? _restoreTimer;
    private readonly object          _timerLock = new();

    public StatusBarService(ISynthtaxHubClient hub)
    {
        _hub = hub;
        _hub.ConnectionStateChanged += OnConnectionStateChanged;
    }

    /// <summary>
    /// Initialisera statusbar-referens. Anropas från <c>SynthtaxPackage.InitializeAsync</c>
    /// efter switch till UI-tråd.
    /// </summary>
    public void Initialize(IVsStatusbar statusBar)
    {
        _statusBar = statusBar;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Publik API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Visa tillfällig text i statusfältet.
    /// Återgår automatiskt till anslutningsstatus efter <paramref name="autoRestoreAfter"/>.
    /// </summary>
    public async Task ShowTextAsync(
        string   text,
        TimeSpan? autoRestoreAfter = null)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        SetText(text);
        StopAnimation();

        if (autoRestoreAfter.HasValue)
            ScheduleRestore(autoRestoreAfter.Value);
    }

    /// <summary>Återgå till anslutningsstatus omedelbart.</summary>
    public async Task RestoreConnectionStatusAsync()
    {
        CancelRestore();
        await ShowConnectionStatusAsync(_hub.State);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Anslutningsstatus-event
    // ═══════════════════════════════════════════════════════════════════════

    private void OnConnectionStateChanged(object? sender, HubConnectionState state)
    {
        _ = ShowConnectionStatusAsync(state);
    }

    private async Task ShowConnectionStatusAsync(HubConnectionState state)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        CancelRestore();

        var (text, animate) = state switch
        {
            HubConnectionState.Connected     => ("● Synthtax: Ansluten",         false),
            HubConnectionState.Connecting    => ("◌ Synthtax: Ansluter…",        true),
            HubConnectionState.Reconnecting  => ("⚠ Synthtax: Återsansluter…",  true),
            HubConnectionState.Disconnected  => ("○ Synthtax: Frånkopplad",      false),
            HubConnectionState.AuthError     => ("🔑 Synthtax: Logga in",         false),
            _                                => ("○ Synthtax",                    false)
        };

        _lastConnectionText = text;
        SetText(text);

        if (animate) StartAnimation();
        else         StopAnimation();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VS Statusfält — lågnivå
    // ═══════════════════════════════════════════════════════════════════════

    private void SetText(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_statusBar is null) return;

        _statusBar.IsFrozen(out int frozen);
        if (frozen != 0) return; // VS har låst statusfältet (t.ex. under build)

        _statusBar.SetText(text);
    }

    private void StartAnimation()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_animationRunning || _statusBar is null) return;

        // VS inbyggd "spinning dots"-animation (ikon 1 = generisk progress)
        object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;
        _statusBar.Animation(1, ref icon);
        _animationRunning = true;
    }

    private void StopAnimation()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_animationRunning || _statusBar is null) return;

        object icon = (short)Microsoft.VisualStudio.Shell.Interop.Constants.SBAI_General;
        _statusBar.Animation(0, ref icon);
        _animationRunning = false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Auto-restore timer
    // ═══════════════════════════════════════════════════════════════════════

    private void ScheduleRestore(TimeSpan delay)
    {
        lock (_timerLock)
        {
            _restoreTimer?.Dispose();
            _restoreTimer = new System.Threading.Timer(
                _ => _ = RestoreConnectionStatusAsync(),
                null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelRestore()
    {
        lock (_timerLock)
        {
            _restoreTimer?.Dispose();
            _restoreTimer = null;
        }
    }

    public void Dispose()
    {
        _hub.ConnectionStateChanged -= OnConnectionStateChanged;
        CancelRestore();
    }
}
