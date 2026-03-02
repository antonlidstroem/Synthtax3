using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Analysis.Plugins;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;
using Synthtax.Core.PromptFactory;

namespace Synthtax.Application.PromptFactory;

// ═══════════════════════════════════════════════════════════════════════════
// PromptFactoryServiceExtensions  —  DI-registrering
// ═══════════════════════════════════════════════════════════════════════════

public static class PromptFactoryServiceExtensions
{
    /// <summary>
    /// Registrerar Fas 6-komponenter.
    ///
    /// <para><b>Anrop i Program.cs:</b>
    /// <code>
    ///   // Fas 1–5 (befintliga)
    ///   builder.Services.AddDomainInfrastructure(builder.Configuration);
    ///   builder.Services.AddPluginCore();
    ///   builder.Services.AddOrchestrator();
    ///   builder.Services.AddFuzzyMatching();
    ///   builder.Services.AddSaasInfrastructure(builder.Configuration);
    ///
    ///   // Fas 6
    ///   builder.Services.AddPromptFactory();
    /// </code>
    /// </para>
    /// </summary>
    public static IServiceCollection AddPromptFactory(this IServiceCollection services)
    {
        // PromptFactoryService — stateless → Singleton
        services.AddSingleton<IPromptFactoryService, PromptFactoryService>();

        // Plugin för arkitektonisk analys
        services.AddSingleton<IAnalysisPlugin, ArchitecturalRefactoringPlugin>();

        return services;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PromptRequest/Response DTOs  —  API-lagret
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Request-body för POST /api/v1/prompts/generate</summary>
public sealed record GeneratePromptApiRequest
{
    /// <summary>"Copilot", "Claude" eller "General".</summary>
    public string Target { get; init; } = "Claude";

    /// <summary>Issue-data — fylls typiskt från BacklogItem.Metadata.</summary>
    public RawIssueDto Issue { get; init; } = new();

    public string? ProjectTechStack      { get; init; }
    public string? ArchitecturePattern   { get; init; }
    public string? CodingConventions     { get; init; }
    public List<RelatedFileDto>? RelatedFiles { get; init; }
}

public sealed record RawIssueDto
{
    public string RuleId    { get; init; } = string.Empty;
    public string FilePath  { get; init; } = string.Empty;
    public int    StartLine { get; init; }
    public int    EndLine   { get; init; }
    public string Snippet   { get; init; } = string.Empty;
    public string Message   { get; init; } = string.Empty;
    public string? Suggestion { get; init; }
    public string Category  { get; init; } = "General";
    public string Severity  { get; init; } = "Medium";
    public string? FixedSnippet { get; init; }
    public bool   IsAutoFixable { get; init; }
    public string? Namespace    { get; init; }
    public string? ClassName    { get; init; }
    public string? MemberName   { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed record RelatedFileDto(string FilePath, string Content, string? Description = null);

/// <summary>Response-body från POST /api/v1/prompts/generate</summary>
public sealed record GeneratePromptApiResponse
{
    public string PromptText        { get; init; } = string.Empty;
    public int    EstimatedTokens   { get; init; }
    public string Target            { get; init; } = string.Empty;
    public string RuleId            { get; init; } = string.Empty;
    public List<string> SuggestedCommands { get; init; } = [];
    public DateTime GeneratedAt     { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// PromptController
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// REST API för Prompt Factory.
///
/// <para><b>Endpoints:</b>
/// <list type="table">
///   <item>POST /api/v1/prompts/generate — generera en prompt för ett issue</item>
///   <item>POST /api/v1/prompts/batch — generera prompts för flera issues</item>
/// </list>
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/prompts")]
[Authorize]
public sealed class PromptController : ControllerBase
{
    private readonly IPromptFactoryService _factory;

    public PromptController(IPromptFactoryService factory)
    {
        _factory = factory;
    }

    // ── POST /generate ─────────────────────────────────────────────────────

    /// <summary>
    /// Genererar en AI-optimerad prompt för ett enskilt issue.
    ///
    /// <para>Vanligaste användningsfallet: Användaren klickar "Generate AI Prompt"
    /// på ett BacklogItem i UI:t och kopierar prompten till Copilot/Claude.</para>
    /// </summary>
    [HttpPost("generate")]
    public ActionResult<GeneratePromptApiResponse> Generate(
        [FromBody] GeneratePromptApiRequest apiRequest)
    {
        if (string.IsNullOrWhiteSpace(apiRequest.Issue.RuleId))
            return BadRequest("Issue.RuleId är obligatoriskt.");

        var issue = MapToRawIssue(apiRequest.Issue);
        if (issue is null)
            return BadRequest("Kunde inte tolka Issue — kontrollera Severity och Scope-fält.");

        if (!Enum.TryParse<PromptTarget>(apiRequest.Target, true, out var target))
            return BadRequest($"Ogiltigt Target: '{apiRequest.Target}'. Tillåtna värden: Copilot, Claude, General.");

        var request = new PromptRequest
        {
            Target             = target,
            Issue              = issue,
            ProjectTechStack   = apiRequest.ProjectTechStack,
            ArchitecturePattern = apiRequest.ArchitecturePattern,
            CodingConventions  = apiRequest.CodingConventions,
            RelatedFiles       = apiRequest.RelatedFiles?
                .Select(f => new RelatedFile(f.FilePath, f.Content, f.Description))
                .ToList()
                .AsReadOnly()
        };

        var result = _factory.Generate(request);

        return Ok(new GeneratePromptApiResponse
        {
            PromptText        = result.PromptText,
            EstimatedTokens   = result.EstimatedTokens,
            Target            = result.Target.ToString(),
            RuleId            = result.RuleId,
            SuggestedCommands = result.SuggestedCommands.ToList(),
            GeneratedAt       = result.GeneratedAt
        });
    }

    // ── POST /batch ────────────────────────────────────────────────────────

    /// <summary>
    /// Genererar prompts för en lista av issues.
    /// Maximalt 50 issues per batch-anrop.
    /// </summary>
    [HttpPost("batch")]
    public ActionResult<List<GeneratePromptApiResponse>> Batch(
        [FromBody] BatchPromptApiRequest apiRequest)
    {
        if (apiRequest.Issues.Count > 50)
            return BadRequest("Max 50 issues per batch-anrop.");

        if (!Enum.TryParse<PromptTarget>(apiRequest.Target, true, out var target))
            return BadRequest($"Ogiltigt Target: '{apiRequest.Target}'.");

        var issues = apiRequest.Issues
            .Select(MapToRawIssue)
            .Where(i => i is not null)
            .Cast<RawIssue>()
            .ToList()
            .AsReadOnly();

        var results = _factory.GenerateBatch(issues, target, apiRequest.ProjectTechStack);

        return Ok(results.Select(r => new GeneratePromptApiResponse
        {
            PromptText      = r.PromptText,
            EstimatedTokens = r.EstimatedTokens,
            Target          = r.Target.ToString(),
            RuleId          = r.RuleId,
            GeneratedAt     = r.GeneratedAt
        }).ToList());
    }

    // ── Mapping ────────────────────────────────────────────────────────────

    private static RawIssue? MapToRawIssue(RawIssueDto dto)
    {
        if (!Enum.TryParse<Severity>(dto.Severity, true, out var severity))
            return null;

        var scope = new LogicalScope
        {
            Namespace  = dto.Namespace,
            ClassName  = dto.ClassName,
            MemberName = dto.MemberName,
            Kind       = dto.MemberName is not null ? ScopeKind.Method : ScopeKind.Class
        };

        return new RawIssue
        {
            RuleId        = dto.RuleId,
            Scope         = scope,
            FilePath      = dto.FilePath,
            StartLine     = dto.StartLine,
            EndLine       = dto.EndLine > 0 ? dto.EndLine : dto.StartLine,
            Snippet       = dto.Snippet,
            Message       = dto.Message,
            Suggestion    = dto.Suggestion,
            Category      = dto.Category,
            Severity      = severity,
            IsAutoFixable = dto.IsAutoFixable,
            FixedSnippet  = dto.FixedSnippet,
            Metadata      = (dto.Metadata ?? []).AsReadOnly()
        };
    }
}

/// <summary>Request-body för POST /api/v1/prompts/batch</summary>
public sealed record BatchPromptApiRequest
{
    public string Target { get; init; } = "Claude";
    public List<RawIssueDto> Issues { get; init; } = [];
    public string? ProjectTechStack { get; init; }
}
