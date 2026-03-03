using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Synthtax.Application.PromptFactory;
using Synthtax.Core.Contracts;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;
using Synthtax.Infrastructure.Data;
using Synthtax.Infrastructure.Services;

// NOTE: CSharpStructuralPlugin lives in Synthtax.Analysis.
// Using alias to disambiguate if needed:
using AppPromptFactory = Synthtax.Application.PromptFactory.PromptFactoryService;

namespace Synthtax.API.Controllers;

// ═══════════════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════════════

public sealed record GeneratePromptRequest(
    Guid         BacklogItemId,
    PromptTarget Target = PromptTarget.Claude);

public sealed record GeneratePromptBothRequest(
    Guid BacklogItemId);

public sealed record GeneratedPromptDto(
    PromptTarget Target,
    string       Title,
    string       Content,
    int          EstimatedTokens,
    string       RuleId,
    DateTime     GeneratedAt);

// ═══════════════════════════════════════════════════════════════════════════
// PromptController
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// REST API för att generera AI-prompts från BacklogItems.
///
/// <para><b>Endpoints:</b>
/// <code>
///   POST /api/v1/prompts/generate         → Single prompt (Copilot ELLER Claude)
///   POST /api/v1/prompts/generate-both    → Båda targets i ett anrop
///   GET  /api/v1/prompts/project/{id}     → Alla öppna issues i ett projekt
/// </code>
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/prompts")]
[Authorize]
public sealed class PromptController : ControllerBase
{
    private readonly IPromptFactoryService _factory;
    private readonly SynthtaxDbContext     _db;
    private readonly ICurrentUserService   _currentUser;

    public PromptController(
        IPromptFactoryService factory,
        SynthtaxDbContext     db,
        ICurrentUserService   currentUser)
    {
        _factory     = factory;
        _db          = db;
        _currentUser = currentUser;
    }

    /// <summary>Genererar en prompt för ett specifikt BacklogItem.</summary>
    [HttpPost("generate")]
    [ProducesResponseType<GeneratedPromptDto>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Generate(
        [FromBody] GeneratePromptRequest req,
        CancellationToken ct)
    {
        var ctx = await BuildContextAsync(req.BacklogItemId, ct);
        if (ctx is null) return NotFound($"BacklogItem {req.BacklogItemId} hittades inte.");

        var prompt = _factory.Generate(ctx, req.Target);
        return Ok(ToDto(prompt));
    }

    /// <summary>Genererar Copilot + Claude-prompt i ett anrop.</summary>
    [HttpPost("generate-both")]
    [ProducesResponseType<object>(200)]
    public async Task<IActionResult> GenerateBoth(
        [FromBody] GeneratePromptBothRequest req,
        CancellationToken ct)
    {
        var ctx = await BuildContextAsync(req.BacklogItemId, ct);
        if (ctx is null) return NotFound($"BacklogItem {req.BacklogItemId} hittades inte.");

        var (copilot, claude) = _factory.GenerateBoth(ctx);
        return Ok(new
        {
            copilot = ToDto(copilot),
            claude  = ToDto(claude)
        });
    }

    /// <summary>
    /// Genererar prompts för alla öppna issues i ett projekt,
    /// sorterade efter Severity descending.
    /// </summary>
    [HttpGet("project/{projectId}")]
    [ProducesResponseType<object>(200)]
    public async Task<IActionResult> GetForProject(
        Guid             projectId,
        [FromQuery] PromptTarget target   = PromptTarget.Claude,
        [FromQuery] int          maxItems = 20,
        CancellationToken ct = default)
    {
        var items = await _db.BacklogItems
            .Include(bi => bi.Rule)
            .Where(bi => bi.ProjectId == projectId &&
                         bi.Status == BacklogStatus.Open)
            .OrderByDescending(bi => bi.EffectiveSeverity)
            .Take(Math.Clamp(maxItems, 1, 100))
            .ToListAsync(ct);

        var contexts = items
            .Select(BuildContextFromBacklogItem)
            .Where(c => c is not null)
            .Cast<PromptContext>()
            .ToList()
            .AsReadOnly();

        var prompts = _factory.GenerateBatch(contexts, target);
        return Ok(prompts.Select(ToDto).ToList());
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private async Task<PromptContext?> BuildContextAsync(Guid backlogItemId, CancellationToken ct)
    {
        var item = await _db.BacklogItems
            .Include(bi => bi.Rule)
            .Include(bi => bi.Project)
            .FirstOrDefaultAsync(bi => bi.Id == backlogItemId, ct);

        return item is null ? null : BuildContextFromBacklogItem(item);
    }

    private static PromptContext? BuildContextFromBacklogItem(BacklogItem item)
    {
        if (item.Rule is null) return null;

        var (filePath, startLine, endLine, snippet, scope) =
            ExtractMetadata(item.Metadata);

        return new PromptContext
        {
            RuleId          = item.RuleId,
            RuleName        = item.Rule.Name,
            RuleDescription = item.Rule.Description ?? item.Rule.Name,
            Category        = item.Rule.Category ?? "General",
            Severity        = item.EffectiveSeverity,
            FilePath        = filePath,
            Snippet         = snippet,
            StartLine       = startLine,
            EndLine         = endLine,
            Namespace       = ExtractScopeField(scope, "namespace"),
            ClassName       = ExtractScopeField(scope, "class"),
            MemberName      = ExtractScopeField(scope, "member"),
            ProjectName     = item.Project?.Name,
            Language        = DetectLanguage(filePath)
        };
    }

    private static (string FilePath, int StartLine, int EndLine, string Snippet, string Scope)
        ExtractMetadata(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return ("unknown", 0, 0, string.Empty, string.Empty);

        try
        {
            var doc  = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("filePath",  out var fp) ? fp.GetString() ?? "" : "",
                root.TryGetProperty("startLine", out var sl) ? sl.GetInt32() : 0,
                root.TryGetProperty("endLine",   out var el) ? el.GetInt32() : 0,
                root.TryGetProperty("snippet",   out var sn) ? sn.GetString() ?? "" : "",
                root.TryGetProperty("scope",     out var sc) ? sc.GetString() ?? "" : ""
            );
        }
        catch { return ("unknown", 0, 0, string.Empty, string.Empty); }
    }

    private static string? ExtractScopeField(string scope, string field)
    {
        if (string.IsNullOrEmpty(scope)) return null;
        var parts = scope.Split("::");
        return field switch
        {
            "namespace" => parts.Length >= 1 ? parts[0] : null,
            "class"     => parts.Length >= 2 ? parts[1] : null,
            "member"    => parts.Length >= 3 ? parts[2].Split('[')[0] : null,
            _           => null
        };
    }

    private static string? DetectLanguage(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".cs"   => "C#",
            ".py"   => "Python",
            ".ts"   => "TypeScript",
            ".js"   => "JavaScript",
            ".java" => "Java",
            ".rb"   => "Ruby",
            _       => null
        };

    private static GeneratedPromptDto ToDto(GeneratedPrompt p) =>
        new(p.Target, p.Title, p.Content, p.EstimatedTokens, p.RuleId, p.GeneratedAt);
}

// ═══════════════════════════════════════════════════════════════════════════
// DI-registrering
// ═══════════════════════════════════════════════════════════════════════════

public static class Fas6ServiceExtensions
{
    /// <summary>
    /// Registrerar alla Fas 6-komponenter (PromptFactory).
    /// Anropa i Program.cs med <c>builder.Services.AddPromptFactory()</c>.
    /// </summary>
    public static IServiceCollection AddPromptFactory(this IServiceCollection services)
    {
        // Explicit fully qualified name for disambiguation
        services.AddSingleton<IPromptFactoryService,
            Synthtax.Application.PromptFactory.PromptFactoryService>();

        // CSharpStructuralPlugin — registreras som IAnalysisPlugin
        // services.AddSingleton<IAnalysisPlugin,
        //     Synthtax.Analysis.Plugin.CSharpStructuralPlugin>();

        return services;
    }
}
