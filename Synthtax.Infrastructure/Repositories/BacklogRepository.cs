using Microsoft.EntityFrameworkCore;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Entities;

namespace Synthtax.Infrastructure.Repositories;

public class BacklogRepository : IBacklogRepository
{
    private readonly SynthtaxDbContext _context;

    public BacklogRepository(SynthtaxDbContext context)
    {
        _context = context;
    }

    public async Task<BacklogItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _context.BacklogItems
            .Include(b => b.CreatedByUser)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

        return item is null ? null : MapToDto(item);
    }

    public async Task<PagedResultDto<BacklogItemDto>> GetPagedAsync(
        Guid tenantId,
        string? userId,
        int page,
        int pageSize,
        BacklogStatus? status = null,
        Priority? priority = null,
        BacklogCategory? category = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BacklogItems
            .Include(b => b.CreatedByUser)
            .Where(b => b.TenantId == tenantId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(b => b.CreatedByUserId == userId);

        if (status.HasValue)
            query = query.Where(b => b.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(b => b.Priority == priority.Value);

        if (category.HasValue)
            query = query.Where(b => b.Category == category.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(b => b.Priority)
            .ThenBy(b => b.Deadline)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResultDto<BacklogItemDto>
        {
            Items = items.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<BacklogSummaryDto> GetSummaryAsync(
        Guid tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.BacklogItems
            .Where(b => b.TenantId == tenantId)
            .AsQueryable();

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(b => b.CreatedByUserId == userId);

        var now = DateTime.UtcNow;

        return new BacklogSummaryDto
        {
            TotalItems = await query.CountAsync(cancellationToken),
            TodoCount = await query.CountAsync(b => b.Status == BacklogStatus.Todo, cancellationToken),
            InProgressCount = await query.CountAsync(b => b.Status == BacklogStatus.InProgress, cancellationToken),
            DoneCount = await query.CountAsync(b => b.Status == BacklogStatus.Done, cancellationToken),
            HighPriorityCount = await query.CountAsync(b => b.Priority == Priority.High && b.Status != BacklogStatus.Done, cancellationToken),
            OverdueCount = await query.CountAsync(b => b.Deadline.HasValue && b.Deadline < now && b.Status != BacklogStatus.Done, cancellationToken)
        };
    }

    public async Task<BacklogItemDto> CreateAsync(
        CreateBacklogItemDto dto,
        string userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var item = new BacklogItem
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            Category = dto.Category,
            Deadline = dto.Deadline,
            Tags = dto.Tags,
            LinkedFilePath = dto.LinkedFilePath,
            Status = BacklogStatus.Todo,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            TenantId = tenantId
        };

        _context.BacklogItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(item.Id, cancellationToken)
               ?? throw new InvalidOperationException("Failed to retrieve created backlog item.");
    }

    public async Task<BacklogItemDto?> UpdateAsync(
        Guid id,
        UpdateBacklogItemDto dto,
        CancellationToken cancellationToken = default)
    {
        var item = await _context.BacklogItems.FindAsync(new object[] { id }, cancellationToken);
        if (item is null) return null;

        if (dto.Title is not null) item.Title = dto.Title;
        if (dto.Description is not null) item.Description = dto.Description;
        if (dto.Priority.HasValue) item.Priority = dto.Priority.Value;
        if (dto.Category.HasValue) item.Category = dto.Category.Value;
        if (dto.Deadline.HasValue) item.Deadline = dto.Deadline;
        if (dto.Tags is not null) item.Tags = dto.Tags;
        if (dto.LinkedFilePath is not null) item.LinkedFilePath = dto.LinkedFilePath;

        if (dto.Status.HasValue)
        {
            item.Status = dto.Status.Value;
            if (dto.Status.Value == BacklogStatus.Done && item.CompletedAt is null)
                item.CompletedAt = DateTime.UtcNow;
            else if (dto.Status.Value != BacklogStatus.Done)
                item.CompletedAt = null;
        }

        item.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _context.BacklogItems.FindAsync(new object[] { id }, cancellationToken);
        if (item is null) return false;

        _context.BacklogItems.Remove(item);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static BacklogItemDto MapToDto(BacklogItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Description = item.Description,
        Status = item.Status,
        Priority = item.Priority,
        Category = item.Category,
        Deadline = item.Deadline,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        CompletedAt = item.CompletedAt,
        Tags = item.Tags,
        LinkedFilePath = item.LinkedFilePath,
        TenantId = item.TenantId,
        CreatedByUserId = item.CreatedByUserId,
        CreatedByUserName = item.CreatedByUser?.UserName
    };
}
