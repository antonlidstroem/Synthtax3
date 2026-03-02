using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.Services;
using Synthtax.Vsix.ToolWindow.ViewModels;

namespace Synthtax.Vsix.SignalR;

// ═══════════════════════════════════════════════════════════════════════════
// BacklogToolWindowViewModelExtension
//
// Implementerar IToolWindowRefreshTarget på BacklogToolWindowViewModel
// via partial class (Fas 7-ViewModel utökas utan att ändra dess fil).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Fas 8-tillägg till <c>BacklogToolWindowViewModel</c> (Fas 7).
///
/// <para>Implementerar <see cref="IToolWindowRefreshTarget"/> via partial class
/// så att Fas 7-filen förblir orörd. Lägg till <c>partial</c> keyword och
/// <c>: IToolWindowRefreshTarget</c> i Fas 7:s klass-deklaration.</para>
///
/// <para><b>Inkrementell uppdatering utan full omladdning:</b>
/// <list type="bullet">
///   <item>Nya issues läggs till i befintlig <c>ObservableCollection</c>.</item>
///   <item>Lösta issues tas bort via fingerprint-matchning.</item>
///   <item>ICollectionView uppdateras med <c>Refresh()</c>.</item>
///   <item>Hälsopoäng och räknare uppdateras utan API-anrop.</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class BacklogToolWindowViewModel : IToolWindowRefreshTarget
{
    // ── IToolWindowRefreshTarget ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ApplyIncrementalUpdateAsync(
        IReadOnlyList<BacklogItemDto> added,
        IReadOnlyList<string>         removed)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        // Ta bort lösta issues (matcha på filsökväg som proxy för fingerprint)
        if (removed.Count > 0)
        {
            var removedSet = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
            var toRemove = _allItems
                .Where(vm => removedSet.Contains(vm.FilePath))
                .ToList();

            foreach (var vm in toRemove)
                _allItems.Remove(vm);

            TotalIssues   = Math.Max(0, TotalIssues - toRemove.Count);
            CriticalCount = _allItems.Count(i => i.Severity == "Critical");
            HighCount     = _allItems.Count(i => i.Severity == "High");
        }

        // Lägg till nya issues (hoppa över dubletter via Id-check)
        if (added.Count > 0)
        {
            var existingIds = new HashSet<Guid>(_allItems.Select(vm => vm.Id));

            foreach (var dto in added)
            {
                if (existingIds.Contains(dto.Id)) continue;
                _allItems.Insert(0, new BacklogItemViewModel(dto)); // nyast först
            }

            TotalIssues   = _allItems.Count;
            CriticalCount = _allItems.Count(i => i.Severity == "Critical");
            HighCount     = _allItems.Count(i => i.Severity == "High");
        }

        // Uppdatera hälsopoäng (approximation — exakt värde pushas i nästa heartbeat)
        if (TotalIssues > 0)
        {
            var critWeight = CriticalCount * 10 + HighCount * 5;
            HealthScore    = Math.Max(0, 100 - critWeight);
            HealthScoreText = $"{HealthScore:F0}/100";
        }

        FilteredItems.Refresh();
        StatusText = $"Uppdaterad {DateTime.Now:HH:mm:ss} — {TotalIssues} issues (realtid)";
    }

    /// <inheritdoc/>
    public void RemoveIssue(Guid issueId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var vm = _allItems.FirstOrDefault(i => i.Id == issueId);
        if (vm is null) return;

        _allItems.Remove(vm);
        TotalIssues   = Math.Max(0, TotalIssues - 1);
        CriticalCount = _allItems.Count(i => i.Severity == "Critical");
        HighCount     = _allItems.Count(i => i.Severity == "High");
        FilteredItems.Refresh();
    }

    /// <inheritdoc/>
    public void UpdateSubscriptionPlan(string newPlan)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SubscriptionPlan = newPlan;
    }

    // ── SignalR-status i ViewModel ────────────────────────────────────────

    [ObservableProperty] private string _signalRStatusText  = "";
    [ObservableProperty] private bool   _isSignalRConnected = false;

    internal void UpdateSignalRStatus(HubConnectionState state)
    {
        IsSignalRConnected = state == HubConnectionState.Connected;
        SignalRStatusText  = state switch
        {
            HubConnectionState.Connected    => "● Realtid",
            HubConnectionState.Connecting   => "◌ Ansluter…",
            HubConnectionState.Reconnecting => "⚠ Återsansluter",
            HubConnectionState.Disconnected => "○ Offline",
            HubConnectionState.AuthError    => "🔑 Ej inloggad",
            _                               => ""
        };
    }
}
