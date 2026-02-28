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
public class ExportController : SynthtaxControllerBase
{
    private readonly IExportService _exportService;
    private readonly IAuditLogRepository _auditLog;

    public ExportController(IExportService exportService, IAuditLogRepository auditLog)
    {
        _exportService = exportService;
        _auditLog      = auditLog;
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        [FromBody] ExportRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ModuleName))
            return BadRequest(new { Message = "ModuleName is required." });

        ExportResultDto result;
        switch (request.Format)
        {
            case ExportFormat.Csv:
                if (request.Data is null)
                    return BadRequest(new { Message = "Data is required for CSV export." });
                var csvData = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<List<Dictionary<string, object>>>(
                        Newtonsoft.Json.JsonConvert.SerializeObject(request.Data))
                    ?? new List<Dictionary<string, object>>();
                result = await _exportService.ExportToCsvAsync(csvData, request.ModuleName, cancellationToken);
                break;

            case ExportFormat.Json:
                result = await _exportService.ExportToJsonAsync(request.Data, request.ModuleName, cancellationToken);
                break;

            case ExportFormat.Pdf:
                return BadRequest(new
                {
                    Message = "Use the module-specific PDF endpoints (e.g. /api/export/pdf/code-analysis) for PDF generation."
                });

            default:
                return BadRequest(new { Message = $"Unsupported export format: {request.Format}" });
        }

        if (!result.Success)
            return StatusCode(500, new { Message = result.ErrorMessage });

        await LogExportAsync(request.ModuleName, request.Format.ToString());
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    [HttpPost("pdf/code-analysis")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportCodeAnalysisPdf(
        [FromBody] CodeAnalysisResultDto data,
        [FromQuery] string language = "sv-SE",
        CancellationToken cancellationToken = default)
    {
        var headers = new[] { "Fil", "Typ", "Rad", "Beskrivning", "Allvarlighet" };
        var rows = data.LongMethods
            .Concat(data.DeadVariables)
            .Concat(data.UnnecessaryUsings)
            .Select(i => new[] { i.FileName, i.IssueType, i.LineNumber.ToString(), i.Description, i.Severity.ToString() });

        var title  = language == "sv-SE" ? "Kodanalys – Rapport" : "Code Analysis Report";
        var result = await _exportService.ExportToPdfAsync("CodeAnalysis", title, rows, headers, language, cancellationToken);
        if (!result.Success) return StatusCode(500, new { Message = result.ErrorMessage });
        await LogExportAsync("CodeAnalysis", "Pdf");
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    [HttpPost("pdf/security")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportSecurityPdf(
        [FromBody] SecurityAnalysisResultDto data,
        [FromQuery] string language = "sv-SE",
        CancellationToken cancellationToken = default)
    {
        var headers = new[] { "Fil", "Kategori", "Titel", "Rad", "Allvarlighet", "Rekommendation" };
        var rows    = data.AllIssues.Select(i => new[] { i.FileName, i.Category, i.Title, i.LineNumber.ToString(), i.Severity.ToString(), i.Recommendation });
        var title   = language == "sv-SE" ? "Säkerhetsanalys – Rapport" : "Security Analysis Report";
        var result  = await _exportService.ExportToPdfAsync("Security", title, rows, headers, language, cancellationToken);
        if (!result.Success) return StatusCode(500, new { Message = result.ErrorMessage });
        await LogExportAsync("Security", "Pdf");
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    [HttpPost("pdf/metrics")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportMetricsPdf(
        [FromBody] MetricsResultDto data,
        [FromQuery] string language = "sv-SE",
        CancellationToken cancellationToken = default)
    {
        var headers = new[] { "Fil", "LOC", "Komplexitet", "Maintainability", "Metoder", "Klasser" };
        var rows    = data.Files.Select(f => new[] { f.FileName, f.LinesOfCode.ToString(), f.AverageCyclomaticComplexity.ToString("F1"), f.MaintainabilityIndex.ToString("F1"), f.NumberOfMethods.ToString(), f.NumberOfClasses.ToString() });
        var title   = language == "sv-SE" ? "Metrics – Rapport" : "Metrics Report";
        var result  = await _exportService.ExportToPdfAsync("Metrics", title, rows, headers, language, cancellationToken);
        if (!result.Success) return StatusCode(500, new { Message = result.ErrorMessage });
        await LogExportAsync("Metrics", "Pdf");
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    [HttpPost("pdf/backlog")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExportBacklogPdf(
        [FromBody] List<BacklogItemDto> items,
        [FromQuery] string language = "sv-SE",
        CancellationToken cancellationToken = default)
    {
        var headers = language == "sv-SE"
            ? new[] { "Titel", "Status", "Prioritet", "Kategori", "Deadline", "Skapad av" }
            : new[] { "Title", "Status", "Priority", "Category", "Deadline", "Created By" };
        var rows   = items.Select(i => new[] { i.Title, i.Status.ToString(), i.Priority.ToString(), i.Category.ToString(), i.Deadline?.ToString("yyyy-MM-dd") ?? "-", i.CreatedByUserName ?? "-" });
        var title  = language == "sv-SE" ? "Backlog – Rapport" : "Backlog Report";
        var result = await _exportService.ExportToPdfAsync("Backlog", title, rows, headers, language, cancellationToken);
        if (!result.Success) return StatusCode(500, new { Message = result.ErrorMessage });
        await LogExportAsync("Backlog", "Pdf");
        return File(result.FileContent!, result.ContentType, result.FileName);
    }

    private async Task LogExportAsync(string moduleName, string format)
        => await _auditLog.LogAsync(
            GetCurrentUserId(), "Export", moduleName, null,
            $"Format: {format}", GetClientIp());
}
