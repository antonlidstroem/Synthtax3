using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class BacklogController : ControllerBase
{
    private readonly IBacklogRepository _backlogRepo;
    private readonly IExportService _exportService;
    private readonly ILogger<BacklogController> _logger;

    public BacklogController(
        IBacklogRepository backlogRepo,
        IExportService exportService,
        ILogger<BacklogController> logger)
    {
        _backlogRepo = backlogRepo;
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>Hämtar backlog-poster paginerat med valfria filter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResultDto<BacklogItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] BacklogStatus? status = null,
        [FromQuery] Priority? priority = null,
        [FromQuery] BacklogCategory? category = null,
        [FromQuery] bool myItemsOnly = false,
        CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var userId = myItemsOnly ? GetCurrentUserId() : null;

        var result = await _backlogRepo.GetPagedAsync(
            tenantId, userId, page, pageSize, status, priority, category, cancellationToken);

        return Ok(result);
    }

    /// <summary>Hämtar backlog-sammanfattning (räkningar per status/prioritet).</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(BacklogSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] bool myItemsOnly = false,
        CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var userId = myItemsOnly ? GetCurrentUserId() : null;
        var summary = await _backlogRepo.GetSummaryAsync(tenantId, userId, cancellationToken);
        return Ok(summary);
    }

    /// <summary>Hämtar en enskild backlog-post.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BacklogItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await _backlogRepo.GetByIdAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    /// <summary>Skapar en ny backlog-post.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BacklogItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBacklogItemDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { Message = "Title is required." });

        var userId = GetCurrentUserId();
        var tenantId = GetTenantId();

        var item = await _backlogRepo.CreateAsync(dto, userId, tenantId, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = item.Id }, item);
    }

    /// <summary>Uppdaterar en befintlig backlog-post.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BacklogItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateBacklogItemDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _backlogRepo.UpdateAsync(id, dto, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Ändrar status på en backlog-post.</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(BacklogItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        var updated = await _backlogRepo.UpdateAsync(id,
            new UpdateBacklogItemDto { Status = dto.Status }, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Tar bort en backlog-post.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await _backlogRepo.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Exporterar backlog till CSV.</summary>
    [HttpGet("export/csv")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCsv(
        CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var all = await _backlogRepo.GetPagedAsync(tenantId, null, 1, 10000, cancellationToken: cancellationToken);
        var result = await _exportService.ExportToCsvAsync(all.Items, "Backlog", cancellationToken);
        if (!result.Success) return StatusCode(500, new { Message = result.ErrorMessage });
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    /// <summary>Exporterar backlog till JSON.</summary>
    [HttpGet("export/json")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportJson(
        CancellationToken cancellationToken = default)
    {
        var tenantId = GetTenantId();
        var all = await _backlogRepo.GetPagedAsync(tenantId, null, 1, 10000, cancellationToken: cancellationToken);
        var result = await _exportService.ExportToJsonAsync(all.Items, "Backlog", cancellationToken);
        if (!result.Success) return StatusCode(500, new { Message = result.ErrorMessage });
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetCurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new UnauthorizedAccessException("User ID claim not found.");

    private Guid GetTenantId()
    {
        var claim = User.FindFirstValue("tenant_id");
        return claim is not null && Guid.TryParse(claim, out var g) ? g : Guid.Empty;
    }
}

public class UpdateStatusDto
{
    public BacklogStatus Status { get; set; }
}
