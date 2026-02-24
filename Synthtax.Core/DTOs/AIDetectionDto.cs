namespace Synthtax.Core.DTOs;

public class AIDetectionSignalDto
{
    public string SignalType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double Weight { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }
    public string? Evidence { get; set; }
}

public class AIDetectionFileResultDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public double AILikelihoodScore { get; set; }   // 0.0 – 1.0
    public string Verdict { get; set; } = string.Empty; // Unlikely / Possible / Probable / Likely
    public List<AIDetectionSignalDto> Signals { get; set; } = new();
}

public class AIDetectionResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public double OverallScore { get; set; }
    public string OverallVerdict { get; set; } = string.Empty;
    public int FilesAnalyzed { get; set; }
    public int FilesWithHighScore { get; set; }
    public List<AIDetectionFileResultDto> FileResults { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class AnalyzeCodeRequestDto
{
    public string Code { get; set; } = string.Empty;
    public string? FileName { get; set; }
}
