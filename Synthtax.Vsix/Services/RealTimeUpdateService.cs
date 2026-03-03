using System.Collections.Concurrent;
using Microsoft.VisualStudio.Shell;
using Synthtax.Realtime.Contracts;
using Synthtax.Vsix.Analyzers;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.Services;

public sealed class RealTimeUpdateService : IDisposable
{
    private readonly ISynthtaxHubClient  _hub;
    private readonly StatusBarService    _statusBar;
    private IToolWindowRefreshTarget?    _toolWindow;

    private readonly ConcurrentDictionary<string, List<BacklogItemDto>> _issueCache
        = new(StringComparer.OrdinalIgnoreCase);

    public RealTimeUpdateService(
        ISynthtaxHubClient hub,
        StatusBarService   statusBar)
    {
        _hub       = hub;
        _statusBar = statusBar;

        _hub.AnalysisUpdated    += OnAnalysisUpdated;
        _hub.IssueStatusChanged += OnIssueStatusChanged;
        _hub.LicenseChanged     += OnLicenseChanged;
    }

    public void RegisterToolWindow(IToolWindowRefreshTarget target)
        => _toolWindow = target;

    public void UnregisterToolWindow()
        => _toolWindow = null;

    // ─── Event-handlers ──────────────────────────────────────────────────────

    private void OnAnalysisUpdated(object? sender, AnalysisUpdatedEvent payload)
        => _ = HandleAnalysisUpdatedAsync(payload);

    private void OnIssueStatusChanged(object? sender, IssueStatusChangedEvent payload)
        => _ = HandleIssueStatusChangedAsync(payload);

    private void OnLicenseChanged(object? sender, LicenseChangedEvent payload)
        => _ = HandleLicenseChangedAsync(payload);

    // ─── Handlers ────────────────────────────────────────────────────────────

    private async Task HandleAnalysisUpdatedAsync(AnalysisUpdatedEvent payload)
    {
        RemoveClosedFromCache(payload.ClosedIssueIds);
        AddNewIssuesToCache(payload.Issues);

        var flatList = FlattenCache();
        SynthtaxDiagnosticProvider.UpdateCache(flatList);

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (_toolWindow is not null)
        {
            await _toolWindow.ApplyIncrementalUpdateAsync(
                added:      payload.Issues.Select(MapToDto).ToList(),
                removedIds: payload.ClosedIssueIds);
        }

        var msg = payload.NewIssueCount switch
        {
            0 when payload.ClosedIssueCount > 0 =>
                $"✅ Synthtax: {payload.ClosedIssueCount} issue{Pl(payload.ClosedIssueCount)} löst i {payload.ProjectName}",
            > 0 when payload.ClosedIssueCount > 0 =>
                $"⚠ Synthtax: +{payload.NewIssueCount} / -{payload.ClosedIssueCount} issues i {payload.ProjectName}",
            > 0 =>
                $"⚠ Synthtax: {payload.NewIssueCount} nytt issue{Pl(payload.NewIssueCount)} i {payload.ProjectName}",
            _ =>
                $"✓ Synthtax: Analys klar — {payload.TotalIssues} öppna issues"
        };

        await _statusBar.ShowTextAsync(msg);
        await Task.Delay(TimeSpan.FromSeconds(8));
        await _statusBar.RestoreConnectionStatusAsync();
    }

    private async Task HandleIssueStatusChangedAsync(IssueStatusChangedEvent payload)
    {
        var wasOpen = payload.OldStatus is "Open" or "Acknowledged" or "InProgress";
        var nowOpen = payload.NewStatus is "Open" or "Acknowledged" or "InProgress";

        if (wasOpen && !nowOpen)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _toolWindow?.RemoveIssue(payload.IssueId);
            await _statusBar.ShowTextAsync(
                $"✅ Issue stängt av {payload.ChangedByUser}",
                autoRestoreAfter: TimeSpan.FromSeconds(5));
        }
    }

    private async Task HandleLicenseChangedAsync(LicenseChangedEvent payload)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _toolWindow?.UpdateSubscriptionPlan(payload.NewPlan);
        await _statusBar.ShowTextAsync(
            $"🔑 Synthtax: Plan ändrad {payload.OldPlan} → {payload.NewPlan}",
            autoRestoreAfter: TimeSpan.FromSeconds(10));
    }

    // ─── Cache-hantering ──────────────────────────────────────────────────────

    private void RemoveClosedFromCache(IReadOnlyList<Guid> closedIds)
    {
        if (closedIds.Count == 0) return;
        var idSet = new HashSet<Guid>(closedIds);
        foreach (var (_, list) in _issueCache)
            lock (list) list.RemoveAll(i => idSet.Contains(i.Id));
    }

    private void AddNewIssuesToCache(IReadOnlyList<HubBacklogItem> issues)
    {
        foreach (var item in issues)
        {
            var key = NormalizeKey(item.FilePath);
            var dto = MapToDto(item);
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

    public void SeedCache(IReadOnlyList<BacklogItemDto> allItems)
    {
        _issueCache.Clear();
        foreach (var dto in allItems)
        {
            var key = NormalizeKey(dto.FilePath);
            _issueCache.AddOrUpdate(
                key,
                _ => [dto],
                (_, list) => { lock (list) { list.Add(dto); return list; } });
        }
    }

    // ─── Hjälpare ─────────────────────────────────────────────────────────────

    private static BacklogItemDto MapToDto(HubBacklogItem h) => new()
    {
        Id            = h.Id,
        RuleId        = h.RuleId,
        Severity      = h.Severity,
        Status        = h.Status,
        FilePath      = h.FilePath,
        StartLine     = h.StartLine,
        Message       = h.Message,
        ClassName     = h.ClassName,
        MemberName    = h.MemberName,
        Namespace     = h.Namespace,
        IsAutoFixable = h.IsAutoFixable,
        Snippet       = h.Snippet    ?? "",
        Suggestion    = h.Suggestion
    };

    private static string NormalizeKey(string path)
        => path.Replace('\\', '/').ToLowerInvariant();

    private static string Pl(int n) => n == 1 ? "" : "s";

    public void Dispose()
    {
        _hub.AnalysisUpdated    -= OnAnalysisUpdated;
        _hub.IssueStatusChanged -= OnIssueStatusChanged;
        _hub.LicenseChanged     -= OnLicenseChanged;
    }
}
