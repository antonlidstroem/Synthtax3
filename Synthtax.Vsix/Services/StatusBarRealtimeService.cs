using System.Collections.Concurrent;
using System.Timers;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Timer = System.Timers.Timer;

namespace Synthtax.Vsix.Services;

/// <summary>
/// Uppdaterar Visual Studios statusfält med Synthtax realtidsstatus.
///
/// <para><b>Design-principer:</b>
/// <list type="bullet">
///   <item>Diskret: tar inte över hela statusfältet — skriver kort text.</item>
///   <item>Självåterställande: visar "Live ●" vid lyckad anslutning,
///         "Offline" 3 sekunder efter bortkoppling och sedan ingenting.</item>
///   <item>Aldrig störande: vid Connected-state visas status bara 5s,
///         sedan rensas texten för att inte konkurrera med Build-output.</item>
///   <item>Progressikon: roterar under Connecting/Reconnecting.</item>
/// </list>
/// </para>
/// </summary>
public sealed class StatusBarRealtimeService : IDisposable
{
    private readonly IVsStatusbar? _statusBar;
    private readonly Timer         _clearTimer;
    private bool                   _isDisposed;

    // Animerad spinner: index roterar varje 300ms under Connecting/Reconnecting
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private int _spinnerFrame;
    private Timer? _spinnerTimer;

    public StatusBarRealtimeService(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _statusBar = serviceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;

        // Timer som rensar statustext efter Connected-notis
        _clearTimer = new Timer(5_000) { AutoReset = false };
        _clearTimer.Elapsed += OnClearTimerElapsed;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Publik API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prenumerera på <see cref="SynthtaxRealtimeService.ConnectionStateChanged"/>
    /// och vidarebefordra hit.
    /// </summary>
    public void OnConnectionStateChanged(object? sender, ConnectionStateSnapshot state)
    {
        StopSpinner();
        _clearTimer.Stop();

        switch (state.State)
        {
            case RealtimeConnectionState.Connecting:
                StartSpinner("Synthtax: Ansluter");
                break;

            case RealtimeConnectionState.Connected:
                SetText("Synthtax: Live ●");
                // Rensa efter 5s — håller inte statusfältet blockerat
                _clearTimer.Start();
                break;

            case RealtimeConnectionState.Reconnecting:
                var retryText = state.NextRetryIn.HasValue
                    ? $"Synthtax: Återansluter ({state.NextRetryIn.Value.TotalSeconds:F0}s)…"
                    : "Synthtax: Återansluter…";
                StartSpinner(retryText);
                break;

            case RealtimeConnectionState.Failed:
                SetText($"Synthtax: ⚠ {state.ErrorMessage ?? "Anslutning misslyckades"}");
                // Feltext stannar kvar tills användaren agerar
                break;

            case RealtimeConnectionState.Disconnected:
                SetText("Synthtax: Offline");
                _clearTimer.Interval = 3_000;
                _clearTimer.Start();
                break;
        }
    }

    /// <summary>
    /// Visar ett kortlivat meddelande (3s) om ett nytt issue inkommit via push.
    /// Anropas av ToolWindowViewModel när AnalysisUpdated tas emot.
    /// </summary>
    public void ShowPushNotification(int newCount, int closedCount, double healthScore)
    {
        if (newCount == 0 && closedCount == 0) return;

        var parts = new List<string>();
        if (newCount   > 0) parts.Add($"+{newCount} nya issues");
        if (closedCount > 0) parts.Add($"{closedCount} stängda");

        SetText($"Synthtax: {string.Join(", ", parts)} · Hälsa {healthScore:F0}/100");

        _clearTimer.Stop();
        _clearTimer.Interval = 6_000;
        _clearTimer.Start();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privat implementering
    // ═══════════════════════════════════════════════════════════════════════

    private void SetText(string text)
    {
        if (_statusBar is null) return;

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _statusBar.IsFrozen(out int frozen);
            if (frozen != 0) _statusBar.FreezeOutput(0);
            _statusBar.SetText(text);
        });
    }

    private void ClearText()
    {
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _statusBar?.SetText("");
        });
    }

    private void StartSpinner(string baseText)
    {
        _spinnerFrame = 0;
        SetText($"{SpinnerFrames[0]} {baseText}");

        _spinnerTimer = new Timer(300) { AutoReset = true };
        _spinnerTimer.Elapsed += (_, _) =>
        {
            _spinnerFrame = (_spinnerFrame + 1) % SpinnerFrames.Length;
            SetText($"{SpinnerFrames[_spinnerFrame]} {baseText}");
        };
        _spinnerTimer.Start();
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Stop();
        _spinnerTimer?.Dispose();
        _spinnerTimer = null;
    }

    private void OnClearTimerElapsed(object? sender, ElapsedEventArgs e) =>
        ClearText();

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopSpinner();
        _clearTimer.Dispose();
    }
}
