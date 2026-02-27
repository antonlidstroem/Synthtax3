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

    public async Task<Guid> CreateSessionAsync(
        string solutionPath,
        string sessionType,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        var session = new AnalysisSession
        {
            Id = Guid.NewGuid(),
            SolutionPath = solutionPath,
            SessionType = sessionType,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(ttl ?? DefaultTtl)
        };

        _context.AnalysisSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        return session.Id;
    }

    public async Task SaveIssuesAsync(
        Guid sessionId,
        IEnumerable<SavedIssueDto> issues,
        CancellationToken cancellationToken = default)
    {
        var issueList = issues.ToList();
        if (issueList.Count == 0) return;

        var entities = issueList.Select(i => new SavedAnalysisIssue
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            FilePath = i.FilePath,
            FileName = i.FileName,
            LineNumber = i.LineNumber,
            EndLineNumber = i.EndLineNumber,
            IssueType = i.IssueType,
            Severity = i.Severity,
            Description = i.Description,
            CodeSnippet = i.CodeSnippet,
            SuggestedFix = i.SuggestedFix,
            FixedCodeSnippet = i.FixedCodeSnippet,
            MethodName = i.MethodName,
            ClassName = i.ClassName,
            IsAutoFixable = i.IsAutoFixable,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        // Batch insert for performance on large projects
        await _context.AnalysisIssues.AddRangeAsync(entities, cancellationToken);

        // Update session issue count
        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is not null)
            session.TotalIssues += issueList.Count;

        await _context.SaveChangesAsync(cancellationToken);
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
            .OrderBy(i => i.FilePath)
            .ThenBy(i => i.LineNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new SessionIssuesResultDto
        {
            Session = MapSessionToDto(session),
            Issues = items.Select(MapIssueToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var expired = await _context.AnalysisSessions
            .Where(s => s.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0) return 0;

        // Cascade delete will remove issues automatically
        _context.AnalysisSessions.RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    public async Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _context.AnalysisSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) return;
        _context.AnalysisSessions.Remove(session);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static AnalysisSessionDto MapSessionToDto(AnalysisSession s) => new()
    {
        Id = s.Id,
        SolutionPath = s.SolutionPath,
        SessionType = s.SessionType,
        CreatedAt = s.CreatedAt,
        ExpiresAt = s.ExpiresAt,
        TotalIssues = s.TotalIssues
    };

    private static SavedIssueDto MapIssueToDto(SavedAnalysisIssue i) => new()
    {
        Id = i.Id,
        SessionId = i.SessionId,
        FilePath = i.FilePath,
        FileName = i.FileName,
        LineNumber = i.LineNumber,
        EndLineNumber = i.EndLineNumber,
        IssueType = i.IssueType,
        Severity = i.Severity,
        Description = i.Description,
        CodeSnippet = i.CodeSnippet,
        SuggestedFix = i.SuggestedFix,
        FixedCodeSnippet = i.FixedCodeSnippet,
        MethodName = i.MethodName,
        ClassName = i.ClassName,
        IsAutoFixable = i.IsAutoFixable,
        CreatedAt = i.CreatedAt
    };
}
