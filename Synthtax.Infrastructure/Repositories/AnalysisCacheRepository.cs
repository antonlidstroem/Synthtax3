using Microsoft.EntityFrameworkCore;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Repositories;

public class AnalysisCacheRepository : IAnalysisCacheService
{
    private readonly AnalysisCacheDbContext _context;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public AnalysisCacheRepository(AnalysisCacheDbContext context)
    {
        _context = context;
    }

    // ── Session management ────────────────────────────────────────────────────

    public async Task<Guid> CreateSessionAsync(
        string solutionPath,
        string sessionType,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var session = new AnalysisSession
        {
            Id         = Guid.NewGuid(),
            SolutionPath = solutionPath,
            SessionType  = sessionType,
            CreatedAt    = DateTime.UtcNow,
            ExpiresAt    = DateTime.UtcNow.Add(ttl ?? DefaultTtl)
        };
        _context.AnalysisSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        return session.Id;
    }

    public async Task<AnalysisSessionDto?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        return session is null ? null : MapSessionToDto(session);
    }

    public async Task<List<AnalysisSessionDto>> ListSessionsAsync(
        string? solutionPath = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AnalysisSessions
            .Where(s => s.ExpiresAt > DateTime.UtcNow)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(solutionPath))
            query = query.Where(s => s.SolutionPath == solutionPath);

        var sessions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return sessions.Select(MapSessionToDto).ToList();
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) return;
        _context.AnalysisSessions.Remove(session);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var expired = await _context.AnalysisSessions
            .Where(s => s.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return 0;
        _context.AnalysisSessions.RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    // ── Issue storage ─────────────────────────────────────────────────────────

    public async Task SaveIssuesAsync(
        Guid sessionId,
        IEnumerable<SavedIssueDto> issues,
        CancellationToken cancellationToken = default)
    {
        var issueList = issues.ToList();
        if (issueList.Count == 0) return;

        var entities = issueList.Select(i => new SavedAnalysisIssue
        {
            Id               = Guid.NewGuid(),
            SessionId        = sessionId,
            FilePath         = i.FilePath,
            FileName         = i.FileName,
            LineNumber       = i.LineNumber,
            EndLineNumber    = i.EndLineNumber,
            IssueType        = i.IssueType,
            Severity         = i.Severity,
            Description      = i.Description,
            CodeSnippet      = i.CodeSnippet,
            SuggestedFix     = i.SuggestedFix,
            FixedCodeSnippet = i.FixedCodeSnippet,
            MethodName       = i.MethodName,
            ClassName        = i.ClassName,
            IsAutoFixable    = i.IsAutoFixable,
            CreatedAt        = DateTime.UtcNow
        }).ToList();

        await _context.AnalysisIssues.AddRangeAsync(entities, cancellationToken);

        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is not null)
            session.TotalIssues += issueList.Count;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<SessionIssuesResultDto> GetIssuesAsync(
        Guid sessionId,
        int page = 1,
        int pageSize = 50,
        string? issueType = null,
        Severity? severity = null,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return new SessionIssuesResultDto { Page = page, PageSize = pageSize };

        var query = _context.AnalysisIssues
            .Where(i => i.SessionId == sessionId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(issueType))
            query = query.Where(i => i.IssueType == issueType);

        if (severity.HasValue)
            query = query.Where(i => i.Severity == severity.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SessionIssuesResultDto
        {
            Session    = MapSessionToDto(session),
            Issues     = items.Select(MapIssueToDto).ToList(),
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize
        };
    }

    /// <summary>
    /// Hämtar en enskild issue med fullständig kodsekvens.
    /// </summary>
    public async Task<SavedIssueDto?> GetIssueByIdAsync(
        Guid issueId,
        CancellationToken cancellationToken = default)
    {
        // Kontrollera att sessionen inte löpt ut via join
        var issue = await _context.AnalysisIssues
            .Join(_context.AnalysisSessions,
                i => i.SessionId,
                s => s.Id,
                (i, s) => new { Issue = i, Session = s })
            .Where(x => x.Issue.Id == issueId && x.Session.ExpiresAt > DateTime.UtcNow)
            .Select(x => x.Issue)
            .FirstOrDefaultAsync(cancellationToken);

        return issue is null ? null : MapIssueToDto(issue);
    }

    /// <summary>
    /// Söker bland alla aktiva (icke-utgångna) issues med fritext och filter.
    /// </summary>
    public async Task<IssueSearchResultDto> SearchIssuesAsync(
        string? query = null,
        string? issueType = null,
        Severity? severity = null,
        bool? autoFixableOnly = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Koppla till session för att filtrera ut utgångna
        var q = _context.AnalysisIssues
            .Join(_context.AnalysisSessions,
                i => i.SessionId,
                s => s.Id,
                (i, s) => new { Issue = i, Session = s })
            .Where(x => x.Session.ExpiresAt > DateTime.UtcNow)
            .Select(x => x.Issue)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(issueType))
            q = q.Where(i => i.IssueType == issueType);

        if (severity.HasValue)
            q = q.Where(i => i.Severity == severity.Value);

        if (autoFixableOnly == true)
            q = q.Where(i => i.IsAutoFixable);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lq = query.ToLower();
            q = q.Where(i =>
                i.Description.ToLower().Contains(lq) ||
                i.CodeSnippet.ToLower().Contains(lq) ||
                i.FileName.ToLower().Contains(lq) ||
                (i.MethodName != null && i.MethodName.ToLower().Contains(lq)) ||
                (i.ClassName  != null && i.ClassName.ToLower().Contains(lq)));
        }

        var totalCount = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.FilePath)
            .ThenBy(i => i.LineNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new IssueSearchResultDto
        {
            Issues          = items.Select(MapIssueToDto).ToList(),
            TotalCount      = totalCount,
            Page            = page,
            PageSize        = pageSize,
            Query           = query,
            IssueTypeFilter = issueType
        };
    }

    /// <summary>
    /// Beräknar aggregerad statistik med SQL GROUP BY – undviker att ladda issues i RAM.
    /// Tidigare beräknades detta via LINQ på upp till 10 000 objekt i minnet.
    /// </summary>
    public async Task<IssueSummaryDto> GetSessionSummaryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
            return new IssueSummaryDto { SessionId = sessionId };

        // GROUP BY Severity
        var bySeverity = await _context.AnalysisIssues
            .Where(i => i.SessionId == sessionId)
            .GroupBy(i => i.Severity)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // GROUP BY IssueType
        var byType = await _context.AnalysisIssues
            .Where(i => i.SessionId == sessionId)
            .GroupBy(i => i.IssueType)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Aggregerade scalar-värden
        var autoFixableCount = await _context.AnalysisIssues
            .CountAsync(i => i.SessionId == sessionId && i.IsAutoFixable, cancellationToken);

        var filesAffected = await _context.AnalysisIssues
            .Where(i => i.SessionId == sessionId)
            .Select(i => i.FilePath)
            .Distinct()
            .CountAsync(cancellationToken);

        return new IssueSummaryDto
        {
            SessionId      = sessionId,
            SessionType    = session.SessionType,
            SolutionPath   = session.SolutionPath,
            CreatedAt      = session.CreatedAt,
            TotalIssues    = session.TotalIssues,
            AutoFixableCount = autoFixableCount,
            FilesAffected  = filesAffected,
            BySeverity     = bySeverity.ToDictionary(x => x.Key.ToString(), x => x.Count),
            ByIssueType    = byType.ToDictionary(x => x.Key, x => x.Count)
        };
    }

    // ── Mapping helpers ───────────────────────────────────────────────────────

    private static AnalysisSessionDto MapSessionToDto(AnalysisSession s) => new()
    {
        Id           = s.Id,
        SolutionPath = s.SolutionPath,
        SessionType  = s.SessionType,
        CreatedAt    = s.CreatedAt,
        ExpiresAt    = s.ExpiresAt,
        TotalIssues  = s.TotalIssues
    };

    private static SavedIssueDto MapIssueToDto(SavedAnalysisIssue i) => new()
    {
        Id               = i.Id,
        SessionId        = i.SessionId,
        FilePath         = i.FilePath,
        FileName         = i.FileName,
        LineNumber       = i.LineNumber,
        EndLineNumber    = i.EndLineNumber,
        IssueType        = i.IssueType,
        Severity         = i.Severity,
        Description      = i.Description,
        CodeSnippet      = i.CodeSnippet,
        SuggestedFix     = i.SuggestedFix,
        FixedCodeSnippet = i.FixedCodeSnippet,
        MethodName       = i.MethodName,
        ClassName        = i.ClassName,
        IsAutoFixable    = i.IsAutoFixable,
        CreatedAt        = i.CreatedAt
    };
}
