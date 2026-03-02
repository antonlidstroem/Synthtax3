// ... (behåll befintliga usings)
using Synthtax.Application.Services; // För IHubPusher och ICurrentUserService

namespace Synthtax.Application.Orchestration;

public sealed class AnalysisOrchestrator : IAnalysisOrchestrator
{
    private readonly SynthtaxDbContext _db;
    private readonly IPluginRegistry _registry;
    private readonly IFileScanner _scanner;
    private readonly SyncEngine _syncEngine;
    private readonly SyncWriter _syncWriter;
    private readonly IHubPusher _hubPusher;          // NY: Fas 8
    private readonly ICurrentUserService _user;     // NY: Fas 8
    private readonly ILogger<AnalysisOrchestrator> _logger;

    public AnalysisOrchestrator(
        SynthtaxDbContext db,
        IPluginRegistry registry,
        IFileScanner scanner,
        SyncEngine syncEngine,
        SyncWriter syncWriter,
        IHubPusher hubPusher,                        // NY
        ICurrentUserService user,                   // NY
        ILogger<AnalysisOrchestrator> logger)
    {
        _db = db;
        _registry = registry;
        _scanner = scanner;
        _syncEngine = syncEngine;
        _syncWriter = syncWriter;
        _hubPusher = hubPusher;                      // NY
        _user = user;                               // NY
        _logger = logger;
    }

    public async Task<OrchestratorResult> RunAsync(OrchestratorRequest request, CancellationToken ct = default)
    {
        var totalSw = Stopwatch.StartNew();
        var errors = new List<string>();
        var sessionId = Guid.NewGuid();

        // ── FAS A: Skanning & analys (IO-tungt, utanför transaktion) ──
        var (scannedIssues, scanDuration) = await RunScanPhaseAsync(request, errors, ct);

        // ── FAS B: Transaktionell sync ──
        OrchestratorResult result;
        var syncSw = Stopwatch.StartNew();

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Vi hämtar diffen och skriver till DB
            result = await RunSyncPhaseAsync(request, sessionId, scannedIssues, errors, scanDuration, ct);

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            syncSw.Stop();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(ex, "Fel i sync-fas — transaktion återrullad.");
            throw;
        }

        // ── FAS C: Realtidsnotis (Pusha till VSIX efter lyckad commit) ──
        // Detta är logiken du hade i extension-metoden tidigare
        await _hubPusher.PushAnalysisUpdatedAsync(new AnalysisUpdatedPayload
        {
            OrganizationId = _user.OrganizationId ?? Guid.Empty,
            ProjectId = request.ProjectId,
            SessionId = sessionId,
            CompletedAt = DateTime.UtcNow,
            NewIssuesCount = result.NewIssues,
            ResolvedIssuesCount = result.ResolvedIssues,
            TotalOpenIssues = result.TotalIssues,
            HealthScore = result.OverallScore,
            // Vi kan mappa de 5 senaste för en "snabbvy" i IDE:n
            NewIssues = result.NewItemsSummary ?? new List<IssueSummary>()
        });

        return result with { TotalDuration = totalSw.Elapsed };
    }

    // ... (RunScanPhaseAsync är oförändrad)

    private async Task<OrchestratorResult> RunSyncPhaseAsync(...)
    {
        // 1. Ladda befintliga items (använd .AsTracking() då vi ska uppdatera dem)
        var existingItems = await _db.BacklogItems
            .Where(bi => bi.ProjectId == request.ProjectId && !bi.IsDeleted)
            .ToDictionaryAsync(bi => bi.Fingerprint, StringComparer.Ordinal, ct);

        // 2. Beräkna diff
        var diff = _syncEngine.Compute(request.ProjectId, scannedIssues, existingItems);

        // 3. Skriv diff (här uppdateras AutoClosed och ReopenedInSessionId från Fas 3)
        // Se till att SyncWriter.WriteAsync returnerar objekten som lagts till/stängts
        var writeResult = await _syncWriter.WriteAsync(diff, request, sessionId, ct);

        // 4. Beräkna KPI:er
        var activeAfterSync = await _db.BacklogItems
            .Where(bi => bi.ProjectId == request.ProjectId && !bi.IsDeleted && bi.Status == BacklogStatus.Open)
            .Select(bi => new ActiveIssueSummary(bi.Id, bi.SeverityOverride ?? bi.Rule.DefaultSeverity))
            .ToListAsync(ct);

        var session = new AnalysisSession { /* ... populera ... */ };
        KpiCalculator.Populate(session, diff, activeAfterSync);
        _db.AnalysisSessions.Add(session);

        return new OrchestratorResult
        {
            // ... populera ...
            OverallScore = session.OverallScore,
            NewItemsSummary = writeResult.AddedItems.Select(i => new IssueSummary(i.Id, i.Title)).ToList()
        };
    }
}