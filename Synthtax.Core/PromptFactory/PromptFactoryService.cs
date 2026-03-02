using System.Text;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;

namespace Synthtax.Core.PromptFactory;

// ═══════════════════════════════════════════════════════════════════════════
// GeneralPromptFormatter  —  balanserat format för övriga AI-verktyg
// ═══════════════════════════════════════════════════════════════════════════

internal sealed class GeneralPromptFormatter
{
    public PromptResult Format(PromptRequest request)
    {
        var issue = request.Issue;
        var sb    = new StringBuilder();

        sb.AppendLine($"## Code Issue: [{issue.RuleId}] — {issue.Category}");
        sb.AppendLine();

        sb.AppendLine("### Problem");
        sb.AppendLine($"**File:** `{issue.FilePath}` (line {issue.StartLine})");
        sb.AppendLine($"**Scope:** {PromptBuilder.BuildScopePath(issue)}");
        sb.AppendLine($"**Severity:** {PromptBuilder.FormatSeverity(issue.Severity)}");
        sb.AppendLine($"**Description:** {issue.Message}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(issue.Snippet))
        {
            sb.AppendLine("### Current Code");
            sb.AppendLine(PromptBuilder.CodeBlock(issue.Snippet));
            sb.AppendLine();
        }

        sb.AppendLine("### What to fix");
        sb.AppendLine(issue.Suggestion ?? issue.Message);
        sb.AppendLine();

        if (issue.IsAutoFixable && !string.IsNullOrWhiteSpace(issue.FixedSnippet))
        {
            sb.AppendLine("### Suggested Fix");
            sb.AppendLine(PromptBuilder.CodeBlock(issue.FixedSnippet));
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectTechStack))
        {
            sb.AppendLine($"### Tech Stack");
            sb.AppendLine(request.ProjectTechStack);
        }

        return new PromptResult
        {
            PromptText = sb.ToString().TrimEnd(),
            Target     = PromptTarget.General,
            RuleId     = issue.RuleId
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PromptFactoryService  —  huvud-implementering
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Förvandlar en <see cref="RawIssue"/> till en AI-optimerad prompt.
/// Delegerar till target-specifika formatters.
///
/// <para>Registreras som Singleton — alla formatters är stateless.</para>
/// </summary>
public sealed class PromptFactoryService : IPromptFactoryService
{
    private readonly CopilotPromptFormatter _copilotFmt = new();
    private readonly ClaudePromptFormatter  _claudeFmt  = new();
    private readonly GeneralPromptFormatter _generalFmt = new();
    private readonly ILogger<PromptFactoryService> _logger;

    public PromptFactoryService(ILogger<PromptFactoryService> logger)
    {
        _logger = logger;
    }

    public PromptResult Generate(PromptRequest request)
    {
        _logger.LogDebug(
            "PromptFactory: genererar {Target}-prompt för [{RuleId}] @ {File}:{Line}",
            request.Target, request.Issue.RuleId,
            request.Issue.FilePath, request.Issue.StartLine);

        var result = request.Target switch
        {
            PromptTarget.Copilot => _copilotFmt.Format(request),
            PromptTarget.Claude  => _claudeFmt.Format(request),
            PromptTarget.General => _generalFmt.Format(request),
            _                    => _generalFmt.Format(request)
        };

        _logger.LogDebug(
            "PromptFactory: genererade ~{Tokens} tokens för [{RuleId}].",
            result.EstimatedTokens, result.RuleId);

        return result;
    }

    public IReadOnlyList<PromptResult> GenerateBatch(
        IReadOnlyList<RawIssue> issues,
        PromptTarget             target,
        string?                  projectTechStack = null)
    {
        var results = new List<PromptResult>(issues.Count);
        foreach (var issue in issues)
        {
            var request = new PromptRequest
            {
                Target           = target,
                Issue            = issue,
                ProjectTechStack = projectTechStack
            };
            results.Add(Generate(request));
        }
        return results.AsReadOnly();
    }
}
