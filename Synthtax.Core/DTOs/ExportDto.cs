using Synthtax.Core.Enums;

namespace Synthtax.Core.DTOs;

public class ExportRequestDto
{
    public string ModuleName { get; set; } = string.Empty;
    public ExportFormat Format { get; set; }
    public object? Data { get; set; }
    public string? Language { get; set; } = "sv-SE";
}

public class ExportResultDto
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[]? FileContent { get; set; }
    public string? ErrorMessage { get; set; }
}
