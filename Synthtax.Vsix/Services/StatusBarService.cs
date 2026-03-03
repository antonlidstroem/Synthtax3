using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.Services;

/// <summary>
/// BUGFIX #3: _animationCookie deklarerades men lagrades aldrig.
/// VS:s Animation-API tar emot cookie via ref-parameter och returnerar
/// den som out — utan att spara den kan animationen aldrig stoppas korrekt.
/// 
/// Lösning: cookie lagras nu och skickas in på exakt samma sätt vid stop.
/// </summary>
public sealed class StatusBarService : IDisposable
{
    private readonly ISynthtaxHubClient       _hub;
    private IVsStatusbar?                     _statusBar;
    private object                            _animationCookie = (short)Constants.SBAI_General;
    private bool                              _animationRunning;
    private string                            _lastConnectionText = "";
    private System.Threading.Timer?           _restoreTimer;
    private readonly object                   _timerLock = new();
    private bool                              _disposed;

    public StatusBarService(ISynthtaxHubClient hub)
    {
        _hub = hub;
        _hub.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public void Initialize(IVsStatusbar statusBar)
        => _statusBar = statusBar;

    public async Task ShowTextAsync(
        string    text,
        TimeSpan? autoRestoreAfter = null)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        SetText(text);
        StopAnimation();
        if (autoRestoreAfter.HasValue)
            ScheduleRestore(autoRestoreAfter.Value);
    }

    public async Task RestoreConnectionStatusAsync()
    {
        CancelRestore();
        await ShowConnectionStatusAsync(_hub.State);
    }

    private void OnConnectionStateChanged(object? sender, HubConnectionState state)
        => _ = ShowConnectionStatusAsync(state);

    private async Task ShowConnectionStatusAsync(HubConnectionState state)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        CancelRestore();

        var (text, animate) = state switch
        {
            HubConnectionState.Connected    => ("● Synthtax: Ansluten",       false),
            HubConnectionState.Connecting   => ("◌ Synthtax: Ansluter…",      true),
            HubConnectionState.Reconnecting => ("⚠ Synthtax: Återsansluter…", true),
            HubConnectionState.Disconnected => ("○ Synthtax: Frånkopplad",    false),
            HubConnectionState.AuthError    => ("🔑 Synthtax: Logga in",       false),
            _                               => ("○ Synthtax",                  false)
        };

        _lastConnectionText = text;
        SetText(text);
        if (animate) StartAnimation();
        else         StopAnimation();
    }

    private void SetText(string text)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_statusBar is null) return;
        _statusBar.IsFrozen(out int frozen);
        if (frozen != 0) return;
        _statusBar.SetText(text);
    }

    private void StartAnimation()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_animationRunning || _statusBar is null) return;

        // BUGFIX: _animationCookie sparas nu via ref så den kan användas vid stop
        _statusBar.Animation(fAnimate: 1, pvIcon: ref _animationCookie);
        _animationRunning = true;
    }

    private void StopAnimation()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_animationRunning || _statusBar is null) return;

        // BUGFIX: samma cookie skickas in för att stoppa rätt animation
        _statusBar.Animation(fAnimate: 0, pvIcon: ref _animationCookie);
        _animationRunning = false;
    }

    private void ScheduleRestore(TimeSpan delay)
    {
        lock (_timerLock)
        {
            _restoreTimer?.Dispose();
            _restoreTimer = new System.Threading.Timer(
                _ => _ = RestoreConnectionStatusAsync(),
                null, delay, System.Threading.Timeout.InfiniteTimeSpan);
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
        if (_disposed) return;
        _disposed = true;
        _hub.ConnectionStateChanged -= OnConnectionStateChanged;
        CancelRestore();
    }
}
