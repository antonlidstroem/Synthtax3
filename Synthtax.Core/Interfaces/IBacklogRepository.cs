using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Interfaces;

/// <summary>
/// Specifikt repository för Backlog-poster.
/// </summary>
public interface IBacklogRepository
{
    Task<BacklogItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResultDto<BacklogItemDto>> GetPagedAsync(Guid tenantId, string? userId, int page, int pageSize, BacklogStatus? status = null, Priority? priority = null, BacklogCategory? category = null, CancellationToken cancellationToken = default);
    Task<BacklogSummaryDto> GetSummaryAsync(Guid tenantId, string? userId = null, CancellationToken cancellationToken = default);
    Task<BacklogItemDto> CreateAsync(CreateBacklogItemDto dto, string userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<BacklogItemDto?> UpdateAsync(Guid id, UpdateBacklogItemDto dto, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Specifikt repository för Audit-logg.
/// </summary>
public interface IAuditLogRepository
{
    Task LogAsync(string userId, string action, string? resourceType = null, string? resourceId = null, string? details = null, string? ipAddress = null, bool success = true, Guid tenantId = default, CancellationToken cancellationToken = default);
    Task<PagedResultDto<AuditLogDto>> GetPagedAsync(AuditLogQueryDto query, Guid tenantId, CancellationToken cancellationToken = default);
}
