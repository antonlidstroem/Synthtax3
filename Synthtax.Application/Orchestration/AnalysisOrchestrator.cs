using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Application.Services;
using Synthtax.Core.Contracts;
using Synthtax.Core.Orchestration;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;


namespace Synthtax.Application.Orchestration;

public sealed class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly SynthtaxDbContext             _db;
    private readonly IPluginRegistry               _registry;
    private readonly IFileScanner                  _scanner;
    private readonly SyncEngine                    _syncEngine;
    private readonly SyncWriter                    _syncWriter;
    private readonly IHubPusher                    _hubPusher;
    private readonly ICurrentUserService           _user;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        SynthtaxDbContext             db,
        IPluginRegistry               registry,
        IFileScanner                  scanner,
        SyncEngine                    syncEngine,
        SyncWriter                    syncWriter,
        IHubPusher                    hubPusher,
        ICurrentUserService           user,
        ILogger<AnalysisOrchestrator> logger)
    {
        _db         = db;
        _registry   = registry;
        _scanner    = scanner;
        _syncEngine = syncEngine;
        _syncWriter = syncWriter;
        _hubPusher  = hubPusher;
        _user       = user;
        _logger     = logger;
    }

    public async Task<OrchestratorResult> RunAsync(
        OrchestratorRequest request,
        CancellationToken   ct = default)
    {
        var totalSw   = Stopwatch.StartNew();
        var errors    = new List<string>();
        var sessionId = Guid.NewGuid();

        var (scannedIssues, scanDuration) = await RunScanPhaseAsync(request, errors, ct);

        OrchestratorResult result;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            result = await RunSyncPhaseAsync(
                request, sessionId, scannedIssues, errors, scanDuration, ct);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Fel i sync-fas — transaktion återrullad.");
            throw;
        }

        await _hubPusher.PushAnalysisUpdatedAsync(new AnalysisUpdatedEvent
        {
            OrganizationId   = _user.OrganizationId ?? Guid.Empty,
            ProjectId        = request.ProjectId,
            ProjectName      = request.ProjectName ?? "",
            SessionId        = sessionId,
            AnalyzedAt       = DateTime.UtcNow,
            NewIssueCount    = result.NewIssues,
            ClosedIssueCount = result.ResolvedIssues,
            TotalIssues      = result.TotalIssues,
            HealthScore      = result.OverallScore,
            Issues           = result.NewItemsSummary,
            ClosedIssueIds   = result.ClosedIssueIds
        }, ct);

        return result with { TotalDuration = totalSw.Elapsed };
    }

    Task<Core.Orchestration.OrchestratorResult> IAnalysisOrchestrator.RunAsync(OrchestratorRequest request, CancellationToken ct)
    {
        throw new NotImplementedException();
    }

    private async Task<(IReadOnlyList<ScannedIssue> Issues, TimeSpan Duration)>
        RunScanPhaseAsync(
            OrchestratorRequest request,
            List<string>        errors,
            CancellationToken   ct)
    {
        var sw     = Stopwatch.StartNew();
        var files  = await _scanner.ScanAsync(request.ProjectId, ct);
        var issues = new List<ScannedIssue>();

        foreach (var plugin in _registry.GetAll())
        {
            try
            {
                var found = await plugin.AnalyzeAsync(files, ct);
                issues.AddRange(found);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plugin {Plugin} misslyckades.", plugin.GetType().Name);
                errors.Add(ex.Message);
            }
        }

        return (issues, sw.Elapsed);
    }

    private async Task<OrchestratorResult> RunSyncPhaseAsync(
        OrchestratorRequest         request,
        Guid                        sessionId,
        IReadOnlyList<ScannedIssue> scannedIssues,
        List<string>                errors,
        TimeSpan                    scanDuration,
        CancellationToken           ct)
    {
        var existingItems = await _db.BacklogItems
            .Where(bi => bi.ProjectId == request.ProjectId && !bi.IsDeleted)
            .ToDictionaryAsync(bi => bi.Fingerprint, StringComparer.Ordinal, ct);

        var diff        = _syncEngine.Compute(request.ProjectId, scannedIssues, existingItems);
        var writeResult = await _syncWriter.WriteAsync(diff, request, sessionId, ct);

        var activeAfterSync = await _db.BacklogItems
            .Where(bi => bi.ProjectId == request.ProjectId
                      && !bi.IsDeleted
                      && bi.Status   == BacklogStatus.Open)
            .Select(bi => new ActiveIssueSummary(
                bi.Id,
                bi.SeverityOverride ?? bi.Rule.DefaultSeverity))
            .ToListAsync(ct);

        var session = new AnalysisSession
        {
            Id           = sessionId,
            ProjectId    = request.ProjectId,
            ScanDuration = scanDuration,
            Errors       = errors
        };

        KpiCalculator.Populate(session, diff, activeAfterSync);
        _db.AnalysisSessions.Add(session);

        return new OrchestratorResult
        {
            OverallScore    = session.OverallScore,
            NewIssues       = writeResult.AddedCount,
            ResolvedIssues  = writeResult.RemovedCount,
            TotalIssues     = activeAfterSync.Count,
            ClosedIssueIds  = writeResult.RemovedItems.Select(i => i.Id).ToList(),
            NewItemsSummary = writeResult.AddedItems
                .Select(i => new HubBacklogItem
                {
                    Id            = i.Id,
                    Title         = i.Title,
                    RuleId        = i.RuleId,
                    Severity      = i.Severity,
                    Status        = BacklogStatus.Open.ToString(),
                    FilePath      = i.FilePath,
                    StartLine     = i.StartLine,
                    Message       = i.Message,
                    ClassName     = i.ClassName,
                    MemberName    = i.MemberName,
                    Namespace     = i.Namespace,
                    IsAutoFixable = i.IsAutoFixable,
                    Snippet       = i.Snippet,
                    Suggestion    = i.Suggestion
                })
                .ToList()
        };
    }
}

