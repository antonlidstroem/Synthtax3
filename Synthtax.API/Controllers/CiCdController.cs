using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Synthtax.API.Services;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Controllers
{

    // ─────────────────────────────────────────────────────────────────────────────
    // CiCdController — designed for use in CI/CD pipelines (GitHub Actions, Azure
    // Pipelines, etc.). Returns exit-code-friendly pass/fail plus threshold details.
    // ─────────────────────────────────────────────────────────────────────────────

    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]  // CI agents often don't have user tokens; secure via API key header instead
    [Produces("application/json")]
    public class CiCdController : ControllerBase
    {
        private readonly ISolutionAnalysisPipeline _pipeline;
        private readonly RepositoryResolverService _resolver;
        private readonly ILogger<CiCdController> _logger;

        public CiCdController(
            ISolutionAnalysisPipeline pipeline,
            RepositoryResolverService resolver,
            ILogger<CiCdController> logger)
        {
            _pipeline = pipeline;
            _resolver = resolver;
            _logger = logger;
        }

        /// <summary>
        /// Entry point for CI/CD analysis gates.
        /// Returns 200 (pass) or 422 (fail) based on threshold violations.
        ///
        /// Example GitHub Actions step:
        ///   curl -f -X POST https://your-api/api/cicd/gate \
        ///     -H "Content-Type: application/json" \
        ///     -d '{"solutionPath":"https://github.com/org/repo","thresholds":{"maxCriticalSecurityIssues":0}}'
        /// </summary>
        [HttpPost("gate")]
        [ProducesResponseType(typeof(CiCdAnalysisResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(CiCdAnalysisResultDto), StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Gate(
            [FromBody] CiCdAnalysisRequestDto request,
            CancellationToken cancellationToken)
        {
            var resolved = await _resolver.ResolveAsync(request.SolutionPath, cancellationToken);
            if (!resolved.Success)
                return BadRequest(new { Message = resolved.ErrorMessage });

            CiCdAnalysisResultDto ciResult;
            try
            {
                var fullResult = await _pipeline.RunFullAnalysisAsync(
                    resolved.LocalPath!,
                    new FullAnalysisOptions
                    {
                        IncludeMetrics = true,
                        IncludeSecurity = true,
                        IncludeCode = true,
                        IncludeCoupling = true,
                        IncludeAIDetection = false
                    },
                    cancellationToken);

                ciResult = EvaluateThresholds(fullResult, request.Thresholds);
                ciResult.FullResult = fullResult;

                if (request.OutputFormat?.Equals("sarif", StringComparison.OrdinalIgnoreCase) == true)
                    ciResult.SarifReport = BuildSarif(fullResult);
            }
            finally
            {
                if (resolved.IsClone) _resolver.Cleanup(resolved.CloneDir);
            }

            _logger.LogInformation(
                "CI/CD gate for {Path}: {Result} ({ViolationCount} violations)",
                request.SolutionPath,
                ciResult.Passed ? "PASSED" : "FAILED",
                ciResult.Violations.Count);

            return ciResult.Passed ? Ok(ciResult) : UnprocessableEntity(ciResult);
        }

        // ── Threshold evaluation ──────────────────────────────────────────────────

        private static CiCdAnalysisResultDto EvaluateThresholds(
            FullAnalysisResultDto full, CiCdThresholdsDto t)
        {
            var result = new CiCdAnalysisResultDto
            {
                SolutionPath = full.SolutionPath,
                AnalyzedAt = full.AnalyzedAt
            };

            void Check(string metric, double actual, double threshold, bool isMaxThreshold = true)
            {
                bool violated = isMaxThreshold ? actual > threshold : actual < threshold;
                if (!violated) return;
                result.Violations.Add(new CiCdThresholdViolationDto
                {
                    Metric = metric,
                    Actual = actual,
                    Threshold = threshold,
                    Message = isMaxThreshold
                        ? $"{metric} is {actual} (max allowed: {threshold})"
                        : $"{metric} is {actual} (min required: {threshold})",
                    Severity = metric.Contains("Critical") || metric.Contains("Security")
                        ? "error" : "warning"
                });
            }

            if (full.Security is not null)
            {
                Check("CriticalSecurityIssues", full.Security.CriticalCount, t.MaxCriticalSecurityIssues);
                Check("HighSecurityIssues", full.Security.HighCount, t.MaxHighSecurityIssues);
            }

            if (full.Metrics is not null)
            {
                Check("MaintainabilityIndex", full.Metrics.OverallMaintainabilityIndex,
                      t.MinMaintainabilityIndex, isMaxThreshold: false);
                Check("AverageCyclomaticComplexity", full.Metrics.OverallCyclomaticComplexity,
                      t.MaxAverageCyclomaticComplexity);
                Check("AverageCognitiveComplexity", full.Metrics.OverallCognitiveComplexity,
                      t.MaxAverageCognitiveComplexity);
            }

            if (full.Coupling is not null)
                Check("AverageInstability", full.Coupling.AverageInstability, t.MaxInstability);

            result.Passed = result.Violations.Count == 0;
            return result;
        }

        // ── SARIF output ──────────────────────────────────────────────────────────

        private static string BuildSarif(FullAnalysisResultDto full)
        {
            // Minimal SARIF 2.1.0 for GitHub code scanning
            var rules = new List<object>();
            var results = new List<object>();

            if (full.Security is not null)
            {
                foreach (var issue in full.Security.AllIssues)
                {
                    results.Add(new
                    {
                        ruleId = issue.IssueType,
                        level = issue.Severity.ToString().ToLower(),
                        message = new { text = issue.Description },
                        locations = new[]
                        {
                        new { physicalLocation = new
                        {
                            artifactLocation = new { uri = issue.FilePath },
                            region           = new { startLine = issue.LineNumber }
                        }}
                    }
                    });
                }
            }

            var sarif = new
            {
                version = "2.1.0",
                @schema = "https://json.schemastore.org/sarif-2.1.0.json",
                runs = new[]
                {
                new
                {
                    tool    = new { driver = new { name = "Synthtax", version = "1.0.0", rules } },
                    results
                }
            }
            };

            return System.Text.Json.JsonSerializer.Serialize(sarif,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

}
