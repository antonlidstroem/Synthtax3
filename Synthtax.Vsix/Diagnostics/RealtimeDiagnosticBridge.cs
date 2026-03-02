using System.Collections.Concurrent;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Realtime.Contracts;
using Synthtax.Vsix.Analyzers;
using Synthtax.Vsix.Client;

namespace Synthtax.Vsix.Diagnostics;

/// <summary>
/// Brygga mellan <see cref="SynthtaxRealtimeService"/> (SignalR-events)
/// och <see cref="SynthtaxDiagnosticProvider"/> (Roslyn Error List / squiggles).
///
/// <para><b>Flöde:</b>
/// <code>
/// SignalR-event (bakgrundstråd)
///    ↓
/// RealtimeDiagnosticBridge.OnXxx()     — sätter ihop DTO-listan
///    ↓
/// SynthtaxDiagnosticProvider.UpdateCache(items)  — atomisk cache-uppdatering
///    ↓
/// RoslynWorkspaceRefresher.RequestRefresh()      — VS Workspace-re-analys
///    ↓
/// Error List uppdateras utan manuell reload
/// </code>
/// </para>
///
/// <para><b>Inkrementell vs komplett update:</b>
/// <list type="bullet">
///   <item><see cref="OnAnalysisUpdated"/> — komplett ny lista pushas → ersätter hela cachen.</item>
///   <item><see cref="OnIssueCreated"/> — enskilt issue adderas inkrementellt.</item>
///   <item><see cref="OnIssueClosed"/> — enskilt issue tas bort från cachen.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RealtimeDiagnosticBridge
{
    // Lokal kopia av alla aktiva issues (trådsäker)
    private readonly ConcurrentDictionary<Guid, BacklogItemDto> _activeIssues = new();

    private readonly RoslynWorkspaceRefresher _refresher;

    public RealtimeDiagnosticBridge(IServiceProvider serviceProvider)
    {
        _refresher = new RoslynWorkspaceRefresher(serviceProvider);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Event-handlers (anropas av SynthtaxRealtimeService)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hanterar <c>AnalysisUpdated</c> — komplett ny issue-lista.
    /// Ersätter hela den lokala cachen.
    /// </summary>
    public void OnAnalysisUpdated(object? sender, AnalysisUpdatedEventArgs e)
    {
        var payload = e.Payload;

        // Bygg ny cache från hub-payloaden
        _activeIssues.Clear();

        foreach (var hubItem in payload.Issues)
        {
            var dto = MapHubItemToDto(hubItem);
            _activeIssues[dto.Id] = dto;
        }

        // Uppdatera DiagnosticProvider-cachen och trigga re-analys
        FlushToRoslyn(GetAffectedFilePaths(payload.Issues));
    }

    /// <summary>
    /// Hanterar <c>IssueCreated</c> — adderar ett nytt issue inkrementellt.
    /// </summary>
    public void OnIssueCreated(object? sender, IssueCreatedEventArgs e)
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
            MemberName = p.MemberName,
            CreatedAt  = DateTime.UtcNow
        };

        _activeIssues[dto.Id] = dto;

        // Trigga re-analys bara för den berörda filen
        FlushToRoslyn([NormalizePath(p.FilePath)]);
    }

    /// <summary>
    /// Hanterar <c>IssueClosed</c> — tar bort issue från cachen.
    /// </summary>
    public void OnIssueClosed(object? sender, IssueClosedEventArgs e)
    {
        if (_activeIssues.TryRemove(e.Payload.IssueId, out var removed))
            FlushToRoslyn([NormalizePath(removed.FilePath)]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privat
    // ═══════════════════════════════════════════════════════════════════════

    private void FlushToRoslyn(IEnumerable<string> affectedPaths)
    {
        // Pusha hela listan till DiagnosticProvider (atomisk cache-swap)
        SynthtaxDiagnosticProvider.UpdateCache(_activeIssues.Values.ToList());

        // Trigga Roslyn-re-analys för de berörda filerna
        _refresher.RequestRefreshForPaths(affectedPaths);
    }

    private static BacklogItemDto MapHubItemToDto(HubBacklogItem h) => new()
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
        Suggestion   = h.Suggestion,
        CreatedAt    = DateTime.UtcNow
    };

    private static IEnumerable<string> GetAffectedFilePaths(
        IReadOnlyList<HubBacklogItem> items) =>
        items.Select(i => NormalizePath(i.FilePath)).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string NormalizePath(string p) =>
        p.Replace('\\', '/').ToLowerInvariant();
}

// ═══════════════════════════════════════════════════════════════════════════
// RoslynWorkspaceRefresher  — trigger för re-analys utan manuell reload
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Triggar Roslyn-workspace-re-analys för specifika filer utan att
/// ladda om eller stänga document.
///
/// <para><b>Strategi:</b>
/// VS erbjuder inget direkt API för "re-run diagnostics on file X".
/// Den stabila workaroundmetoden är att skicka ett
/// <c>IVsSolution.OnAfterRenameProject</c>-equivalent-event via
/// <c>IVsFileChangeEx</c> — dock är det över-invasivt.
///
/// Istället används <c>IVsRunningDocumentTable</c> för att hitta
/// öppna dokument och <c>IVsTextLines.ReloadFile()</c> om filen
/// är öppen. Stängda filer re-analyseras automatiskt nästa gång
/// de öppnas (cachen är då redan uppdaterad).</para>
/// </summary>
internal sealed class RoslynWorkspaceRefresher
{
    private readonly IServiceProvider _serviceProvider;

    // Debounce: batcha flera snabba re-requests till en enda re-analys
    private readonly System.Timers.Timer _debounceTimer;
    private readonly ConcurrentBag<string> _pendingPaths = new();

    public RoslynWorkspaceRefresher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _debounceTimer   = new System.Timers.Timer(250) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;
    }

    public void RequestRefreshForPaths(IEnumerable<string> normalizedPaths)
    {
        foreach (var p in normalizedPaths) _pendingPaths.Add(p);

        // Debounce — batcha burst av events
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_pendingPaths.TryTake(out var p)) paths.Add(p);

        if (paths.Count == 0) return;

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            RefreshOpenDocuments(paths);
        });
    }

    private void RefreshOpenDocuments(IReadOnlySet<string> normalizedPaths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var rdt = _serviceProvider.GetService(typeof(IVsRunningDocumentTable))
                  as IVsRunningDocumentTable;
        if (rdt is null) return;

        // Iterera alla öppna dokument och hitta de som berörs
        rdt.GetRunningDocumentsEnum(out var pEnum);
        var cookies = new uint[1];

        while (pEnum.Next(1, cookies, out uint fetched) == 0 && fetched == 1)
        {
            rdt.GetDocumentInfo(cookies[0],
                out _,     // grfRDTFlags
                out _,     // rdReadLocks
                out _,     // dwEditLocks
                out var moniker,
                out _,     // pHier
                out _,     // itemid
                out _);    // ppunkDocData

            if (moniker is null) continue;

            var normalized = NormalizePath(moniker);
            var fileName   = System.IO.Path.GetFileName(normalized);

            // Matcha på filnamn (relativ sökväg-jämförelse)
            if (normalizedPaths.Any(p =>
                    normalized.EndsWith(p, StringComparison.OrdinalIgnoreCase)
                    || p.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)))
            {
                // Signalera att filen är "smutsig" → Roslyn kör om diagnostik
                var fileChange = _serviceProvider.GetService(typeof(SVsFileChangeEx))
                                 as IVsFileChangeEx;
                fileChange?.SyncFile(moniker);
            }
        }
    }

    private static string NormalizePath(string p) =>
        p.Replace('\\', '/').ToLowerInvariant();
}
