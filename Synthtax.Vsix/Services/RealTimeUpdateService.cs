using System.Collections.Concurrent;
using Microsoft.VisualStudio.Shell;
using Synthtax.Shared.SignalR;
using Synthtax.Vsix.Analyzers;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.Services;

/// <summary>
/// Abonnerar på <see cref="ISynthtaxHubClient"/>-events och
/// propagerar ändringar till:
/// <list type="bullet">
///   <item><see cref="SynthtaxDiagnosticProvider"/> — uppdaterar squiggles + Error List.</item>
///   <item><see cref="IToolWindowRefreshTarget"/> — uppdaterar Tool Window utan omladdning.</item>
///   <item><see cref="StatusBarService"/> — visar kortfattat analysresultat i statusfältet.</item>
/// </list>
///
/// <para><b>Trådsäkerhet:</b>
/// Alla uppdateringar av UI-beroende tjänster sker via
/// <c>JoinableTaskFactory.SwitchToMainThreadAsync</c>.
/// DiagnosticProvider-cache är <c>ConcurrentDictionary</c> och kan
/// uppdateras från vilken tråd som helst.</para>
/// </summary>
public sealed class RealTimeUpdateService : IDisposable
{
    private readonly ISynthtaxHubClient          _hub;
    private readonly StatusBarService            _statusBar;
    private IToolWindowRefreshTarget?            _toolWindow;

    // Lokal kopia av alla kända issues (fil→issue-lista)
    // Uppdateras inkrementellt vid varje AnalysisUpdated-event
    private readonly ConcurrentDictionary<string, List<BacklogItemDto>> _issueCache
        = new(StringComparer.OrdinalIgnoreCase);

    public RealTimeUpdateService(
        ISynthtaxHubClient hub,
        StatusBarService   statusBar)
    {
        _hub       = hub;
        _statusBar = statusBar;

        // Prenumerera på hub-events
        _hub.AnalysisUpdated    += OnAnalysisUpdated;
        _hub.IssueStatusChanged += OnIssueStatusChanged;
        _hub.LicenseChanged     += OnLicenseChanged;
    }

    /// <summary>
    /// Registrerar Tool Window som mottagare av realtidsuppdateringar.
    /// Anropas från <c>BacklogToolWindow.OnToolWindowCreated()</c>.
    /// </summary>
    public void RegisterToolWindow(IToolWindowRefreshTarget target)
        => _toolWindow = target;

    public void UnregisterToolWindow()
        => _toolWindow = null;

    // ═══════════════════════════════════════════════════════════════════════
    // Event-handlers
    // ═══════════════════════════════════════════════════════════════════════

    private void OnAnalysisUpdated(object? sender, AnalysisUpdatedPayload payload)
    {
        _ = HandleAnalysisUpdatedAsync(payload);
    }

    private void OnIssueStatusChanged(object? sender, IssueStatusChangedPayload payload)
    {
        _ = HandleIssueStatusChangedAsync(payload);
    }

    private void OnLicenseChanged(object? sender, LicenseChangedPayload payload)
    {
        _ = HandleLicenseChangedAsync(payload);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Uppdateringslogik — AnalysisUpdated
    // ═══════════════════════════════════════════════════════════════════════

    private async Task HandleAnalysisUpdatedAsync(AnalysisUpdatedPayload payload)
    {
        // 1. Ta bort lösta issues från cache (squiggles försvinner direkt)
        RemoveResolvedFromCache(payload.ResolvedFingerprints);

        // 2. Lägg till nya issues i cache
        AddNewIssuesToCache(payload.NewIssues);

        // 3. Uppdatera DiagnosticProvider → Error List + squiggles
        var flatList = FlattenCache();
        SynthtaxDiagnosticProvider.UpdateCache(flatList);

        // 4. Uppdatera Tool Window inkrementellt (UI-tråd krävs)
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (_toolWindow is not null)
        {
            await _toolWindow.ApplyIncrementalUpdateAsync(
                added:   payload.NewIssues.Select(MapToDto).ToList(),
                removed: payload.ResolvedFingerprints);
        }

        // 5. Statusfält-notis
        var msg = payload.NewIssuesCount switch
        {
            0 when payload.ResolvedIssuesCount > 0 =>
                $"✅ Synthtax: {payload.ResolvedIssuesCount} issue{Pl(payload.ResolvedIssuesCount)} löst i {payload.ProjectName}",
            > 0 when payload.ResolvedIssuesCount > 0 =>
                $"⚠ Synthtax: +{payload.NewIssuesCount} / -{payload.ResolvedIssuesCount} issues i {payload.ProjectName}",
            > 0 =>
                $"⚠ Synthtax: {payload.NewIssuesCount} nytt issue{Pl(payload.NewIssuesCount)} i {payload.ProjectName}",
            _ =>
                $"✓ Synthtax: Analys klar — {payload.TotalOpenIssues} öppna issues"
        };

        await _statusBar.ShowTextAsync(msg);

        // Återgå till anslutningsstatus efter 8 s
        await Task.Delay(TimeSpan.FromSeconds(8));
        await _statusBar.RestoreConnectionStatusAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IssueStatusChanged — ett ärende uppdaterat av teammedlem
    // ═══════════════════════════════════════════════════════════════════════

    private async Task HandleIssueStatusChangedAsync(IssueStatusChangedPayload payload)
    {
        var wasOpen  = payload.OldStatus is "Open" or "Acknowledged" or "InProgress";
        var nowOpen  = payload.NewStatus is "Open" or "Acknowledged" or "InProgress";

        // Om ärendet stängdes → ta bort från Tool Window
        if (wasOpen && !nowOpen)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _toolWindow?.RemoveIssue(payload.IssueId);

            await _statusBar.ShowTextAsync(
                $"✅ Issue stängt av {payload.ChangedByUser}",
                autoRestoreAfter: TimeSpan.FromSeconds(5));
        }
        // Om det öppnades igen → Tool Window uppdateras vid nästa full refresh
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LicenseChanged
    // ═══════════════════════════════════════════════════════════════════════

    private async Task HandleLicenseChangedAsync(LicenseChangedPayload payload)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _toolWindow?.UpdateSubscriptionPlan(payload.NewPlan);

        await _statusBar.ShowTextAsync(
            $"🔑 Synthtax: Plan ändrad {payload.OldPlan} → {payload.NewPlan}",
            autoRestoreAfter: TimeSpan.FromSeconds(10));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cache-hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private void RemoveResolvedFromCache(IReadOnlyList<string> fingerprints)
    {
        if (fingerprints.Count == 0) return;
        var fpSet = new HashSet<string>(fingerprints, StringComparer.Ordinal);

        foreach (var (key, list) in _issueCache)
        {
            lock (list)
                list.RemoveAll(i => fpSet.Contains(i.FilePath)); // fingerprint är FilePath i stub
        }
    }

    private void AddNewIssuesToCache(IReadOnlyList<IssueSummary> issues)
    {
        foreach (var s in issues)
        {
            var key = s.FilePath.Replace('\\', '/').ToLowerInvariant();
            var dto = MapToDto(s);
            _issueCache.AddOrUpdate(
                key,
                _ => [dto],
                (_, list) => { lock (list) { list.Add(dto); return list; } });
        }
    }

    private IReadOnlyList<BacklogItemDto> FlattenCache()
    {
        var result = new List<BacklogItemDto>();
        foreach (var list in _issueCache.Values)
            lock (list) result.AddRange(list);
        return result;
    }

    /// <summary>Initialisera cache från en komplett backlog-hämtning (t.ex. vid start).</summary>
    public void SeedCache(IReadOnlyList<BacklogItemDto> allItems)
    {
        _issueCache.Clear();
        foreach (var dto in allItems)
        {
            var key = dto.FilePath.Replace('\\', '/').ToLowerInvariant();
            _issueCache.AddOrUpdate(
                key,
                _ => [dto],
                (_, list) => { lock (list) { list.Add(dto); return list; } });
        }
    }

    private static BacklogItemDto MapToDto(IssueSummary s) => new()
    {
        Id         = s.Id,
        RuleId     = s.RuleId,
        Severity   = s.Severity,
        FilePath   = s.FilePath,
        StartLine  = s.StartLine,
        Message    = s.Message,
        ClassName  = s.ClassName,
        MemberName = s.MemberName,
        Status     = "Open"
    };

    private static string Pl(int n) => n == 1 ? "" : "s";

    public void Dispose()
    {
        _hub.AnalysisUpdated    -= OnAnalysisUpdated;
        _hub.IssueStatusChanged -= OnIssueStatusChanged;
        _hub.LicenseChanged     -= OnLicenseChanged;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// IToolWindowRefreshTarget  —  kontraktet mot ToolWindowViewModel
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kontrakt som <c>BacklogToolWindowViewModel</c> implementerar
/// för att ta emot inkrementella realtidsuppdateringar.
/// Separerar RealTimeUpdateService från WPF-beroenden.
/// </summary>
public interface IToolWindowRefreshTarget
{
    /// <summary>
    /// Lägg till nya och ta bort lösta issues utan att ladda om hela listan.
    /// </summary>
    Task ApplyIncrementalUpdateAsync(
        IReadOnlyList<BacklogItemDto> added,
        IReadOnlyList<string>         removed);

    /// <summary>Ta bort ett enskilt ärende (IssueStatusChanged → stängt).</summary>
    void RemoveIssue(Guid issueId);

    /// <summary>Uppdatera plan-badge i Tool Window-headern.</summary>
    void UpdateSubscriptionPlan(string newPlan);
}
