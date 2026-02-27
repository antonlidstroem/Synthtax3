namespace Synthtax.Core.DTOs;

public class FileMetricsDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public int LinesOfCode { get; set; }
    public int LinesOfComments { get; set; }
    public int BlankLines { get; set; }
    public double AverageCyclomaticComplexity { get; set; }

    /// <summary>NEW: Sonar-style cognitive complexity average across all methods.</summary>
    public double AverageCognitiveComplexity { get; set; }

    public double MaintainabilityIndex { get; set; }
    public int NumberOfMethods { get; set; }
    public int NumberOfClasses { get; set; }
    public List<MethodMetricsDto> Methods { get; set; } = new();
}

public class MethodMetricsDto
{
    public string MethodName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public int LinesOfCode { get; set; }

    // Existing: Cyclomatic Complexity
    public int CyclomaticComplexity { get; set; }

    // NEW: Cognitive Complexity (nullable for backward compatibility)
    public int? CognitiveComplexity { get; set; }

    public double MaintainabilityIndex { get; set; }
}






public class MetricsTrendPointDto
{
    public DateTime Date { get; set; }
    public double AverageMaintainabilityIndex { get; set; }
    public double AverageCyclomaticComplexity { get; set; }
    public int TotalLinesOfCode { get; set; }
}

public class MetricsResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public int TotalLinesOfCode { get; set; }
    public double OverallMaintainabilityIndex { get; set; }
    public double OverallCyclomaticComplexity { get; set; }

    /// <summary>NEW: Overall cognitive complexity average.</summary>
    public double OverallCognitiveComplexity { get; set; }

    public int TotalFiles { get; set; }
    public int TotalMethods { get; set; }
    public List<FileMetricsDto> Files { get; set; } = new();
    public List<MetricsTrendPointDto> Trend { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
