using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Orchestration;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;
using Synthtax.Infrastructure.Data;

namespace Synthtax.Application.Orchestration;

/// <summary>
/// Koordinerar ett komplett analysflöde:
///
/// <code>
///   1. Skanna filer (IFileScanner)
///   2. Analysera via plugins (IPluginRegistry)
///   BEGIN TRANSACTION
///     3. Ladda befintliga BacklogItems för projektet
///     4. Beräkna diff (SyncEngine)
///     5. Skriv diff till DB (SyncWriter — standard eller bulk beroende på volym)
///     6. Beräkna KPI:er (KpiCalculator)
///     7. Spara AnalysisSession med KPI:er
///   COMMIT
/// </code>
///
/// <para>Om ett undantag kastas efter att transaktionen öppnats rullas den tillbaka automatiskt.
/// Parsningsfel i enskilda filer avbryter inte hela körningen — de loggas och inkluderas
/// i <c>OrchestratorResult.Errors</c>.</para>
/// </summary>
public sealed class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly SynthtaxDbContext          _db;
    private readonly IPluginRegistry            _registry;
    private readonly IFileScanner               _scanner;
    private readonly SyncEngine                 _syncEngine;
    private readonly SyncWriter                 _syncWriter;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        SynthtaxDbContext             db,
        IPluginRegistry               registry,
        IFileScanner                  scanner,
        SyncEngine                    syncEngine,
        SyncWriter                    syncWriter,
        ILogger<AnalysisOrchestrator> logger)
    {
        _db         = db;
        _registry   = registry;
        _scanner    = scanner;
        _syncEngine = syncEngine;
        _syncWriter = syncWriter;
        _logger     = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Publik API
    // ═══════════════════════════════════════════════════════════════════════

    public async Task<OrchestratorResult> RunAsync(
        OrchestratorRequest request,
        CancellationToken   ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        var errors  = new List<string>();
        var sessionId = Guid.NewGuid();

        _logger.LogInformation(
            "Orchestrator startar. Projekt={ProjectId} Session={SessionId}",
            request.ProjectId, sessionId);

        // ── FAS A: Skanning & analys (utanför transaktion) ────────────────
        var (scannedIssues, scanDuration) = await RunScanPhaseAsync(request, errors, ct);

        _logger.LogInformation(
            "Scan klar: {IssueCount} issues hittade på {Ms}ms.",
            scannedIssues.Count, scanDuration.TotalMilliseconds);

        // ── FAS B: Transaktionell sync ─────────────────────────────────────
        OrchestratorResult result;
        var syncSw = Stopwatch.StartNew();

        await using var transaction = await _db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            result = await RunSyncPhaseAsync(
                request, sessionId, scannedIssues, errors, scanDuration, ct);

            await transaction.CommitAsync(ct);
            syncSw.Stop();

            _logger.LogInformation(
                "Sync commitad. New={New} Resolved={Resolved} Total={Total} Score={Score:F1} ({Ms}ms)",
                result.NewIssues, result.ResolvedIssues, result.TotalIssues,
                result.OverallScore, syncSw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogWarning("Orchestrator avbruten — transaktion återrullad.");
            throw;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Fel i sync-fas — transaktion återrullad.");
            throw;
        }

        totalSw.Stop();
        return result with
        {
            TotalDuration = totalSw.Elapsed,
            ScanDuration  = scanDuration,
            SyncDuration  = syncSw.Elapsed,
            Errors        = errors.AsReadOnly()
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FAS A: Skanning
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<(List<RawIssue> Issues, TimeSpan Duration)> RunScanPhaseAsync(
        OrchestratorRequest request,
        List<string>        errors,
        CancellationToken   ct)
    {
        var sw         = Stopwatch.StartNew();
        var allIssues  = new List<RawIssue>();

        // Samla alla filändelser från registrerade plugins
        var supportedExts = _registry.GetAll()
            .SelectMany(p => p.SupportedExtensions)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await foreach (var file in _scanner.ScanAsync(
            request.ProjectRootPath,
            supportedExts,
            request.FileExtensionFilter,
            ct))
        {
            ct.ThrowIfCancellationRequested();

            var plugins = _registry.GetFor(file.Extension);
            if (plugins.Count == 0) continue;

            string content;
            try
            {
                content = await File.ReadAllTextAsync(file.AbsolutePath, ct);
            }
            catch (Exception ex)
            {
                var msg = $"Kunde inte läsa {file.RelativePath}: {ex.Message}";
                errors.Add(msg);
                _logger.LogWarning("{Msg}", msg);
                continue;
            }

            var analysisRequest = new AnalysisRequest
            {
                ProjectId       = request.ProjectId,
                FileContent     = content,
                FilePath        = file.RelativePath,
                ProjectRootPath = request.ProjectRootPath,
                EnabledRuleIds  = request.EnabledRuleIds,
                CancellationToken = ct
            };

            foreach (var plugin in plugins)
            {
                try
                {
                    var pluginResult = await plugin.AnalyzeAsync(analysisRequest);
                    allIssues.AddRange(pluginResult.Issues);

                    if (pluginResult.HasErrors)
                        foreach (var e in pluginResult.Errors)
                            errors.Add($"[{plugin.PluginId}] {file.RelativePath}: {e}");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    var msg = $"Plugin '{plugin.PluginId}' kraschade på {file.RelativePath}: {ex.Message}";
                    errors.Add(msg);
                    _logger.LogError(ex, "{Msg}", msg);
                }
            }
        }

        sw.Stop();
        return (allIssues, sw.Elapsed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FAS B: Transaktionell sync (kallas inuti öppen transaktion)
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<OrchestratorResult> RunSyncPhaseAsync(
        OrchestratorRequest request,
        Guid                sessionId,
        List<RawIssue>      scannedIssues,
        List<string>        errors,
        TimeSpan            scanDuration,
        CancellationToken   ct)
    {
        // ── 1. Ladda befintliga items — läs med UPDLOCK för att undvika race conditions ──
        var existingItems = await _db.BacklogItems
            .IgnoreQueryFilters()           // inkludera soft-deletade (så fingerprint-kollision syns)
            .Where(bi => bi.ProjectId == request.ProjectId && !bi.IsDeleted)
            .ToDictionaryAsync(bi => bi.Fingerprint, StringComparer.Ordinal, ct);

        var activeItemCount = existingItems.Values.Count(bi =>
            BacklogItemStatus.IsActive(bi.Status));

        _logger.LogDebug(
            "Laddade {Total} befintliga items ({Active} aktiva) för projekt {ProjectId}.",
            existingItems.Count, activeItemCount, request.ProjectId);

        // ── 2. Beräkna diff (rent, ingen DB) ─────────────────────────────
        var diff = _syncEngine.Compute(request.ProjectId, scannedIssues, existingItems);

        _logger.LogDebug(
            "Diff: +{New} nya, {Match} matchade, {Reopen} återöppnade, {Close} auto-stängda.",
            diff.NewCount, diff.ToUpdate.Count, diff.ToReopen.Count, diff.AutoCloseCount);

        // ── 3. Skriv diff till DB ─────────────────────────────────────────
        await _syncWriter.WriteAsync(
            diff, request.ProjectId, request.TenantId, sessionId,
            activeItemCount, request.BulkThreshold, ct);

        // ── 4. Beräkna KPI:er efter att sync är skriven ──────────────────
        var activeAfterSync = await _db.BacklogItems
            .Where(bi => bi.ProjectId == request.ProjectId
                      && !bi.IsDeleted
                      && (bi.Status == BacklogStatus.Open
                          || bi.Status == BacklogStatus.Acknowledged
                          || bi.Status == BacklogStatus.InProgress))
            .Select(bi => new ActiveIssueSummary(
                bi.Id,
                bi.SeverityOverride ?? bi.Rule.DefaultSeverity))
            .ToListAsync(ct);

        // ── 5. Spara AnalysisSession ──────────────────────────────────────
        var session = new AnalysisSession
        {
            Id        = sessionId,
            ProjectId = request.ProjectId,
            Timestamp = DateTime.UtcNow,
            CommitSha = request.CommitSha,
            DurationMs = (long)scanDuration.TotalMilliseconds
        };

        KpiCalculator.Populate(session, diff, activeAfterSync);
        _db.AnalysisSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return new OrchestratorResult
        {
            SessionId      = sessionId,
            ProjectId      = request.ProjectId,
            Success        = errors.Count == 0,
            NewIssues      = session.NewIssues,
            ResolvedIssues = session.ResolvedIssues,
            TotalIssues    = session.TotalIssues,
            ReopenedIssues = diff.ToReopen.Count,
            OverallScore   = session.OverallScore,
            Errors         = errors.AsReadOnly()
        };
    }
}
