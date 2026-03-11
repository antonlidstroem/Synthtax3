namespace Synthtax.Core.DTOs;

/// <summary>Aggregated result produced by SolutionAnalysisPipeline — one solution load, all analyses.</summary>
public class FullAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }

    public CodeAnalysisResultDto? Code { get; set; }
    public SecurityAnalysisResultDto? Security { get; set; }
    public MetricsResultDto? Metrics { get; set; }
    public CouplingAnalysisResultDto? Coupling { get; set; }
    public AIDetectionResultDto? AIDetection { get; set; }

    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Count > 0;
}

// ─────────────────────────────────────────────────────────
// CI/CD integration DTOs
// ─────────────────────────────────────────────────────────

public class CiCdAnalysisRequestDto
{
    /// <summary>Path or GitHub URL to the repository / solution.</summary>
    public string? SolutionPath { get; set; }

    /// <summary>Analysis will FAIL the build if any threshold is exceeded.</summary>
    public CiCdThresholdsDto Thresholds { get; set; } = new();

    /// <summary>SARIF, JSON or Text.</summary>
    public string OutputFormat { get; set; } = "json";
}

public class CiCdThresholdsDto
{
    public int MaxCriticalSecurityIssues { get; set; } = 0;
    public int MaxHighSecurityIssues { get; set; } = 5;
    public double MinMaintainabilityIndex { get; set; } = 40.0;
    public double MaxAverageCyclomaticComplexity { get; set; } = 10.0;
    public double MaxAverageCognitiveComplexity { get; set; } = 15.0;
    public double MaxInstability { get; set; } = 0.85;
    public double MaxAILikelihoodScore { get; set; } = 1.0; // disabled by default
}

public class CiCdAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public bool Passed { get; set; }
    public List<CiCdThresholdViolationDto> Violations { get; set; } = new();
    public FullAnalysisResultDto? FullResult { get; set; }
    public string? SarifReport { get; set; }
}

public class CiCdThresholdViolationDto
{
    public string Metric { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double Actual { get; set; }
    public double Threshold { get; set; }
    public string Severity { get; set; } = "error";
}
