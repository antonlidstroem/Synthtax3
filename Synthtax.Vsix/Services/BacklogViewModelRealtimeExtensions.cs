using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.VisualStudio.Shell;
using Synthtax.Realtime.Contracts;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.CodeFixes;

namespace Synthtax.Vsix.ToolWindow.ViewModels;

/// <summary>
/// Utökar <c>BacklogToolWindowViewModel</c> med SignalR-händelsehantering.
///
/// <para>Registreras som partial class för att separera realtids-logiken
/// från bas-ViewModel-koden. Kompileras med
/// <c>Synthtax.Vsix.ToolWindow.ViewModels</c>-namnrymden och ska
/// läggas till i bakre halvan av <c>BacklogToolWindowViewModel.cs</c>.</para>
///
/// <para><b>Anslutningsschema:</b>
/// <code>
///   realtimeService.AnalysisUpdated    → OnAnalysisUpdated()
///   realtimeService.IssueCreated       → OnIssueCreated()
///   realtimeService.IssueClosed        → OnIssueClosed()
///   realtimeService.HealthScoreUpdated → OnHealthScoreUpdated()
///   realtimeService.ConnectionStateChanged → OnConnectionStateChanged()
/// </code>
/// </para>
/// </summary>
public sealed partial class BacklogToolWindowViewModel : ObservableObject
{
    // ── Realtidsstatus (bindbar) ───────────────────────────────────────────
    [ObservableProperty] private string  _realtimeStatus  = "";
    [ObservableProperty] private bool    _isRealtimeLive  = false;
    [ObservableProperty] private string  _realtimeDotColor = "#95A5A6"; // Grå = offline

    // Referens till RealtimeService (sätts av BacklogToolWindow)
    private Services.SynthtaxRealtimeService? _realtimeService;
    private Services.StatusBarRealtimeService? _statusBarService;

    // ═══════════════════════════════════════════════════════════════════════
    // Registrering
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Anropas av <c>BacklogToolWindow.OnToolWindowCreated()</c> för att
    /// koppla ViewModeln till realtidstjänsterna.
    /// </summary>
    public void AttachRealtimeServices(
        Services.SynthtaxRealtimeService realtimeSvc,
        Services.StatusBarRealtimeService statusBarSvc)
    {
        // Avregistrera eventuella gamla subscriptions
        DetachRealtimeServices();

        _realtimeService  = realtimeSvc;
        _statusBarService = statusBarSvc;

        realtimeSvc.AnalysisUpdated      += OnAnalysisUpdated;
        realtimeSvc.IssueCreated         += OnIssueCreated;
        realtimeSvc.IssueClosed          += OnIssueClosed;
        realtimeSvc.HealthScoreUpdated   += OnHealthScoreUpdated;
        realtimeSvc.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public void DetachRealtimeServices()
    {
        if (_realtimeService is null) return;
        _realtimeService.AnalysisUpdated      -= OnAnalysisUpdated;
        _realtimeService.IssueCreated         -= OnIssueCreated;
        _realtimeService.IssueClosed          -= OnIssueClosed;
        _realtimeService.HealthScoreUpdated   -= OnHealthScoreUpdated;
        _realtimeService.ConnectionStateChanged -= OnConnectionStateChanged;
        _realtimeService  = null;
        _statusBarService = null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Händelsehanterare (körs alltid på UI-tråden — säkerställs av RealtimeService)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Komplett ny issue-lista pushad.
    /// Uppdaterar hela Tool Window-listan och Error List utan manuell reload.
    /// </summary>
    private void OnAnalysisUpdated(object? sender, AnalysisUpdatedEventArgs e)
    {
        var payload = e.Payload;

        // ── Uppdatera hälsodata ────────────────────────────────────────────
        ProjectName     = payload.ProjectName;
        HealthScore     = payload.HealthScore;
        HealthScoreText = $"{payload.HealthScore:F0}/100";
        TotalIssues     = payload.TotalIssues;

        // ── Rebuild itemlistan ────────────────────────────────────────────
        // Befintliga items: behåll, uppdatera ändrade; addera nya; ta bort stängda
        var newIds  = payload.Issues.Select(i => i.Id).ToHashSet();
        var viewMap = _allItems.ToDictionary(v => v.Id);

        // Ta bort försvunna items
        var toRemove = viewMap.Keys
            .Where(id => !newIds.Contains(id))
            .ToList();
        foreach (var id in toRemove)
            if (viewMap.TryGetValue(id, out var vm))
                _allItems.Remove(vm);

        // Addera nya items
        foreach (var hubItem in payload.Issues)
        {
            if (!viewMap.ContainsKey(hubItem.Id))
                _allItems.Add(new BacklogItemViewModel(MapHubToDto(hubItem)));
        }

        // ── Extern cache (CodeFix + DiagnosticProvider) ───────────────────
        var dtos = payload.Issues.Select(MapHubToDto).ToList();
        CodeFixes.BacklogItemCache.Update(dtos);

        // ── Notifiera ─────────────────────────────────────────────────────
        if (payload.HasChanges)
        {
            StatusText = $"🔄 Live-uppdatering {DateTime.Now:HH:mm:ss} — "
                + $"+{payload.NewIssueCount} nya · {payload.ClosedIssueCount} stängda";

            _statusBarService?.ShowPushNotification(
                payload.NewIssueCount, payload.ClosedIssueCount, payload.HealthScore);
        }
        else
        {
            StatusText = $"✓ Inga förändringar {DateTime.Now:HH:mm:ss}";
        }
    }

    /// <summary>
    /// Enskilt nytt issue — adderar en rad utan att störa listan i övrigt.
    /// </summary>
    private void OnIssueCreated(object? sender, IssueCreatedEventArgs e)
    {
        var p = e.Payload;

        var dto = new BacklogItemDto
        {
            Id         = p.IssueId,
            RuleId     = p.RuleId,
            Severity   = p.Severity,
            Status     = "Open",
            FilePath   = p.FilePath,
            StartLine  = p.StartLine,
            Message    = p.Message,
            ClassName  = p.ClassName,
            MemberName = p.MemberName
        };

        // Dublettkontroll
        if (_allItems.All(v => v.Id != p.IssueId))
        {
            _allItems.Add(new BacklogItemViewModel(dto));
            TotalIssues = _allItems.Count;
            StatusText  = $"🔴 Nytt issue: [{p.RuleId}] {p.Message[..Math.Min(50, p.Message.Length)]}";
        }
    }

    /// <summary>Issue auto-stängt — tar bort raden direkt.</summary>
    private void OnIssueClosed(object? sender, IssueClosedEventArgs e)
    {
        var existing = _allItems.FirstOrDefault(v => v.Id == e.Payload.IssueId);
        if (existing is not null)
        {
            _allItems.Remove(existing);
            TotalIssues = _allItems.Count;
            StatusText  = $"✅ Issue stängt: [{e.Payload.RuleId}] ({e.Payload.Reason})";
        }
    }

    /// <summary>Hälsopoängen uppdaterad — uppdaterar progress-baren.</summary>
    private void OnHealthScoreUpdated(object? sender, HealthScoreUpdatedEventArgs e)
    {
        var p = e.Payload;
        HealthScore     = p.NewScore;
        HealthScoreText = $"{p.NewScore:F0}/100";
        TotalIssues     = p.TotalIssues;
        CriticalCount   = p.CriticalCount;
        HighCount       = p.HighCount;

        var arrow = p.Delta >= 0 ? "↑" : "↓";
        StatusText = $"{arrow} Hälsa {p.OldScore:F0}→{p.NewScore:F0}/100";
    }

    /// <summary>
    /// Anslutningsstatus förändrad — uppdaterar realtids-indikatorn i Tool Window-headern.
    /// </summary>
    private void OnConnectionStateChanged(object? sender, ConnectionStateSnapshot state)
    {
        IsRealtimeLive = state.State == RealtimeConnectionState.Connected;

        (RealtimeStatus, RealtimeDotColor) = state.State switch
        {
            RealtimeConnectionState.Connected    => ("Live", "#2ECC71"),  // Grön
            RealtimeConnectionState.Connecting   => ("Ansluter…", "#F39C12"), // Gul
            RealtimeConnectionState.Reconnecting => ($"Åter ({state.RetryAttempt})", "#E67E22"),
            RealtimeConnectionState.Failed       => ("⚠ Offline", "#E74C3C"),  // Röd
            _                                    => ("Offline", "#95A5A6")      // Grå
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mappning
    // ═══════════════════════════════════════════════════════════════════════

    private static BacklogItemDto MapHubToDto(HubBacklogItem h) => new()
    {
        Id           = h.Id,
        RuleId       = h.RuleId,
        Severity     = h.Severity,
        Status       = h.Status,
        FilePath     = h.FilePath,
        StartLine    = h.StartLine,
        Message      = h.Message,
        ClassName    = h.ClassName,
        MemberName   = h.MemberName,
        IsAutoFixable = h.IsAutoFixable,
        Snippet      = h.Snippet ?? "",
        Suggestion   = h.Suggestion
    };
}
