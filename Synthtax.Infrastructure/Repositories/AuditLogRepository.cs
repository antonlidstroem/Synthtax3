using Microsoft.EntityFrameworkCore;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly SynthtaxDbContext _context;

    public AuditLogRepository(SynthtaxDbContext context)
    {
        _context = context;
    }

    public async Task LogAsync(
        string userId,
        string action,
        string? resourceType = null,
        string? resourceId = null,
        string? details = null,
        string? ipAddress = null,
        bool success = true,
        Guid tenantId = default,
        CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Details = details,
            IpAddress = ipAddress,
            Success = success,
            OccurredAt = DateTime.UtcNow,
            TenantId = tenantId
        };

        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResultDto<AuditLogDto>> GetPagedAsync(
        AuditLogQueryDto query,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var q = _context.AuditLogs
            .Include(a => a.User)
            .Where(a => a.TenantId == tenantId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query.UserId))
            q = q.Where(a => a.UserId == query.UserId);

        if (!string.IsNullOrEmpty(query.Action))
            q = q.Where(a => a.Action.Contains(query.Action));

        if (query.From.HasValue)
            q = q.Where(a => a.OccurredAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(a => a.OccurredAt <= query.To.Value);

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(a => a.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResultDto<AuditLogDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    private static AuditLogDto MapToDto(AuditLog log) => new()
    {
        Id = log.Id,
        UserId = log.UserId ?? string.Empty,
        UserName = log.User?.UserName,
        Action = log.Action,
        ResourceType = log.ResourceType,
        ResourceId = log.ResourceId,
        Details = log.Details,
        IpAddress = log.IpAddress,
        Success = log.Success,
        OccurredAt = log.OccurredAt,
        TenantId = log.TenantId
    };
}
