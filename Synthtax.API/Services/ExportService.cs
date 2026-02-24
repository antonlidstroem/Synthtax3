using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services;

public class ExportService : IExportService
{
    private readonly ILogger<ExportService> _logger;

    static ExportService()
    {
        // QuestPDF community license (free for open-source / internal tools)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ExportService(ILogger<ExportService> logger)
    {
        _logger = logger;
    }

    public async Task<ExportResultDto> ExportToCsvAsync<T>(
        IEnumerable<T> data,
        string moduleName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new MemoryStream();
            await using var writer = new StreamWriter(stream, leaveOpen: true);

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                NewLine = Environment.NewLine
            };

            await using var csv = new CsvWriter(writer, config);
            await csv.WriteRecordsAsync(data, cancellationToken);
            await writer.FlushAsync(cancellationToken);

            return new ExportResultDto
            {
                Success = true,
                FileName = GenerateFileName(moduleName, ExportFormat.Csv),
                ContentType = "text/csv",
                FileContent = stream.ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV export failed for module {Module}", moduleName);
            return new ExportResultDto { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<ExportResultDto> ExportToJsonAsync<T>(
        T data,
        string moduleName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc
            };

            var json = JsonConvert.SerializeObject(data, settings);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            return await Task.FromResult(new ExportResultDto
            {
                Success = true,
                FileName = GenerateFileName(moduleName, ExportFormat.Json),
                ContentType = "application/json",
                FileContent = bytes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON export failed for module {Module}", moduleName);
            return new ExportResultDto { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<ExportResultDto> ExportToPdfAsync(
        string moduleName,
        string title,
        IEnumerable<string[]> rows,
        string[] headers,
        string language = "sv-SE",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rowList = rows.ToList();
            var generatedLabel = language == "sv-SE" ? "Genererad" : "Generated";
            var pageLabel = language == "sv-SE" ? "Sida" : "Page";
            var totalLabel = language == "sv-SE" ? "Totalt antal rader" : "Total rows";

            var bytes = await Task.Run(() =>
            {
                return Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(40);
                        page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                        // ── Header ────────────────────────────────────────
                        page.Header().Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Text(title)
                                    .SemiBold().FontSize(16).FontColor(Colors.Blue.Darken3);

                                row.ConstantItem(200).AlignRight()
                                    .Text($"{generatedLabel}: {DateTime.Now:yyyy-MM-dd HH:mm}")
                                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                            });

                            col.Item().PaddingTop(4).BorderBottom(1)
                                .BorderColor(Colors.Blue.Darken3).Text(string.Empty);
                        });

                        // ── Content ───────────────────────────────────────
                        page.Content().PaddingTop(10).Column(col =>
                        {
                            // Summary line
                            col.Item().PaddingBottom(8)
                                .Text($"{totalLabel}: {rowList.Count}")
                                .FontSize(9).FontColor(Colors.Grey.Darken2);

                            // Table
                            col.Item().Table(table =>
                            {
                                // Columns – distribute evenly
                                table.ColumnsDefinition(cols =>
                                {
                                    for (int i = 0; i < headers.Length; i++)
                                        cols.RelativeColumn();
                                });

                                // Header row
                                foreach (var header in headers)
                                {
                                    table.Header(h =>
                                    {
                                        h.Cell().Background(Colors.Blue.Darken3)
                                            .Padding(5)
                                            .Text(header)
                                            .FontColor(Colors.White)
                                            .SemiBold()
                                            .FontSize(8);
                                    });
                                }

                                // Data rows
                                var isAlternate = false;
                                foreach (var row in rowList)
                                {
                                    var bg = isAlternate
                                        ? Colors.Blue.Lighten5
                                        : Colors.White;
                                    isAlternate = !isAlternate;

                                    for (int i = 0; i < headers.Length; i++)
                                    {
                                        var cellValue = i < row.Length ? row[i] : string.Empty;
                                        table.Cell().Background(bg).Padding(4)
                                            .Text(cellValue).FontSize(8);
                                    }
                                }
                            });
                        });

                        // ── Footer ────────────────────────────────────────
                        page.Footer().AlignRight()
                            .Text(txt =>
                            {
                                txt.Span($"{pageLabel} ").FontSize(8).FontColor(Colors.Grey.Darken1);
                                txt.CurrentPageNumber().FontSize(8);
                                txt.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
                                txt.TotalPages().FontSize(8);
                            });
                    });
                }).GeneratePdf();
            }, cancellationToken);

            return new ExportResultDto
            {
                Success = true,
                FileName = GenerateFileName(moduleName, ExportFormat.Pdf),
                ContentType = "application/pdf",
                FileContent = bytes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF export failed for module {Module}", moduleName);
            return new ExportResultDto { Success = false, ErrorMessage = ex.Message };
        }
    }

    public string GenerateFileName(string moduleName, ExportFormat format)
    {
        var date = DateTime.Now.ToString("yyyyMMdd");
        var ext = format switch
        {
            ExportFormat.Csv => "csv",
            ExportFormat.Json => "json",
            ExportFormat.Pdf => "pdf",
            _ => "bin"
        };
        var safeName = string.Concat(moduleName.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return $"{safeName}_{date}.{ext}";
    }
}
