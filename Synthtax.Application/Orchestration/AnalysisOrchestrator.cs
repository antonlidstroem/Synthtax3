using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Orchestration;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;


namespace Synthtax.Application.Orchestration;

public sealed class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly SynthtaxDbContext _db;
    private readonly IPluginRegistry _registry;
    private readonly IFileScanner _scanner;
    private readonly SyncEngine _syncEngine;
    private readonly SyncWriter _syncWriter;
    private readonly IHubPusher _hubPusher;
    private readonly ICurrentUserService _user;
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        SynthtaxDbContext db,
        IPluginRegistry registry,
        IFileScanner scanner,
        SyncEngine syncEngine,
        SyncWriter syncWriter,
        IHubPusher hubPusher,
        ICurrentUserService user,
        ILogger<AnalysisOrchestrator> logger)
    {
        _db = db;
        _registry = registry;
        _scanner = scanner;
        _syncEngine = syncEngine;
        _syncWriter = syncWriter;
        _hubPusher = hubPusher;
        _user = user;
        _logger = logger;
    }

    public async Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        var errors = new List<string>();
        var sessionId = Guid.NewGuid();

        // FAS A: Skanning (Utanför transaktion)
        var (scannedIssues, scanDuration) = await RunScanPhaseAsync(request, errors, ct);

        // FAS B: Transaktionell Sync
        OrchestratorResult result;
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            result = await RunSyncPhaseAsync(request, sessionId, scannedIssues, errors, scanDuration, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Fel vid synkronisering av projekt {ProjectId}", request.ProjectId);
            throw;
        }

        // FAS C: SignalR Push (Fas 8)
        await _hubPusher.PushAnalysisUpdatedAsync(new AnalysisUpdatedPayload
        {
            OrganizationId = _user.OrganizationId ?? Guid.Empty,
            ProjectId = request.ProjectId,
            SessionId = sessionId,
            CompletedAt = DateTime.UtcNow,
            NewIssuesCount = result.NewIssues,
            ResolvedIssuesCount = result.ResolvedIssues,
            TotalOpenIssues = result.TotalIssues,
            HealthScore = result.OverallScore
        });

        return result with { TotalDuration = totalSw.Elapsed };
    }

    private async Task<(List<RawIssue> Issues, TimeSpan Duration)> RunScanPhaseAsync(OrchestratorRequest request, List<string> errors, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // ... (Logik för skanning från din chunk_0)
        return (new List<RawIssue>(), sw.Elapsed); // Förenklat för exemplet
    }

    private async Task<OrchestratorResult> RunSyncPhaseAsync(OrchestratorRequest request, Guid sessionId, List<RawIssue> scannedIssues, List<string> errors, TimeSpan scanDuration, CancellationToken ct)
    {
        var existingItems = await _db.BacklogItems
            .Where(bi => bi.ProjectId == request.ProjectId && !bi.IsDeleted)
            .ToDictionaryAsync(bi => bi.Fingerprint, StringComparer.Ordinal, ct);

        var diff = _syncEngine.Compute(request.ProjectId, scannedIssues, existingItems);
        var writeResult = await _syncWriter.WriteAsync(diff, request, sessionId, ct);

        var session = new AnalysisSession
        {
            Id = sessionId,
            ProjectId = request.ProjectId,
            Timestamp = DateTime.UtcNow
        };

        // Spara sessionen
        _db.AnalysisSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return new OrchestratorResult
        {
            SessionId = sessionId,
            ProjectId = request.ProjectId,
            Success = errors.Count == 0,
            NewIssues = diff.NewCount,
            ResolvedIssues = diff.AutoCloseCount,
            TotalIssues = existingItems.Count + diff.NewCount - diff.AutoCloseCount
        };
    }
}