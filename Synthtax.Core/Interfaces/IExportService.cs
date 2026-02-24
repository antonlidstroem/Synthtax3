using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;

namespace Synthtax.Core.Interfaces;

public interface IExportService
{
    /// <summary>
    /// Exporterar data till CSV.
    /// </summary>
    Task<ExportResultDto> ExportToCsvAsync<T>(IEnumerable<T> data, string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exporterar data till JSON.
    /// </summary>
    Task<ExportResultDto> ExportToJsonAsync<T>(T data, string moduleName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exporterar data till PDF med QuestPDF.
    /// </summary>
    Task<ExportResultDto> ExportToPdfAsync(string moduleName, string title, IEnumerable<string[]> rows, string[] headers, string language = "sv-SE", CancellationToken cancellationToken = default);

    /// <summary>
    /// Genererar filnamn enligt konventionen {ModuleName}_{yyyyMMdd}.{ext}
    /// </summary>
    string GenerateFileName(string moduleName, ExportFormat format);
}
