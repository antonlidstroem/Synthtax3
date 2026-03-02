using Synthtax.Core.Contracts;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;

namespace Synthtax.Core.PromptFactory;

// ═══════════════════════════════════════════════════════════════════════════
// PromptTarget — vilken AI som är mottagare
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Anger vilken AI-assistent prompten riktar sig till.
/// Styr format, längd och kontext-nivå.
/// </summary>
public enum PromptTarget
{
    /// <summary>
    /// GitHub Copilot i editorn.
    /// Format: Kompakt inline-kommentar eller ett-radsinstruktion.
    /// Max ~150 tokens. Prioriterar: "vad ska fixas" + kodraden.
    /// </summary>
    Copilot  = 0,

    /// <summary>
    /// Claude (eller annan fulltext-AI).
    /// Format: Fullständig "Technical Spec" med arkitektonisk kontext,
    /// syfte, begränsningar och exempelkod.
    /// Max ~800 tokens.
    /// </summary>
    Claude   = 1,

    /// <summary>
    /// Generic — ren markdown utan AI-specifik framing.
    /// Användbart för export till ticket-system.
    /// </summary>
    Generic  = 2
}

// ═══════════════════════════════════════════════════════════════════════════
// GeneratedPrompt — resultatet av PromptFactory
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// En genererad AI-prompt för ett specifikt BacklogItem eller RawIssue.
/// </summary>
public sealed record GeneratedPrompt
{
    /// <summary>AI-mottagaren som prompten är anpassad för.</summary>
    public required PromptTarget Target { get; init; }

    /// <summary>Den färdiga prompt-texten, redo att klistras in eller skickas.</summary>
    public required string Content { get; init; }

    /// <summary>Uppskattad tokenlängd (tecken / 4).</summary>
    public int EstimatedTokens => Content.Length / 4;

    /// <summary>RuleId som triggade prompt-genereringen.</summary>
    public required string RuleId { get; init; }

    /// <summary>Tidpunkt då prompten genererades.</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Kort rubrik, lämplig som clipboard-rubrik eller notis-titel.</summary>
    public required string Title { get; init; }
}

// ═══════════════════════════════════════════════════════════════════════════
// PromptContext — indata till PromptFactory
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// All kontext som PromptFactory behöver för att bygga en meningsfull prompt.
/// Kombinerar issue-data med arkitektonisk kontext.
/// </summary>
public sealed record PromptContext
{
    // ── Issue-data ─────────────────────────────────────────────────────────

    public required string RuleId        { get; init; }
    public required string RuleName      { get; init; }
    public required string RuleDescription { get; init; }
    public required string Category      { get; init; }
    public required Severity Severity    { get; init; }

    public required string FilePath      { get; init; }
    public required string Snippet       { get; init; }
    public          string? Suggestion   { get; init; }
    public          string? FixedSnippet { get; init; }
    public          bool    IsAutoFixable { get; init; }

    public required int StartLine { get; init; }
    public required int EndLine   { get; init; }

    // ── Semantisk plats ────────────────────────────────────────────────────

    public string? Namespace   { get; init; }
    public string? ClassName   { get; init; }
    public string? MemberName  { get; init; }
    public string? ScopeKind   { get; init; }

    // ── Projektkontext (valfri — berikar Claude-prompten) ─────────────────

    /// <summary>Projektets namn, t.ex. "Synthtax.API".</summary>
    public string? ProjectName       { get; init; }

    /// <summary>Programmeringsspråk, t.ex. "C#" eller "Python".</summary>
    public string? Language          { get; init; }

    /// <summary>Relaterade regler i samma fil (för kors-referens i Claude-prompt).</summary>
    public IReadOnlyList<string> RelatedRuleIds { get; init; } = [];

    /// <summary>Antal öppna issues med samma regel i projektet.</summary>
    public int SameRuleOpenCount { get; init; }

    // ── Fabriksmetoder ────────────────────────────────────────────────────

    /// <summary>Skapar PromptContext direkt från ett RawIssue.</summary>
    public static PromptContext FromRawIssue(
        RawIssue issue,
        string   ruleName,
        string   ruleDescription,
        string?  projectName = null,
        string?  language    = null)
    => new()
    {
        RuleId          = issue.RuleId,
        RuleName        = ruleName,
        RuleDescription = ruleDescription,
        Category        = issue.Category,
        Severity        = issue.Severity,
        FilePath        = issue.FilePath,
        Snippet         = issue.Snippet,
        Suggestion      = issue.Suggestion,
        FixedSnippet    = issue.FixedSnippet,
        IsAutoFixable   = issue.IsAutoFixable,
        StartLine       = issue.StartLine,
        EndLine         = issue.EndLine,
        Namespace       = issue.Scope.Namespace,
        ClassName       = issue.Scope.ClassName,
        MemberName      = issue.Scope.MemberName,
        ScopeKind       = issue.Scope.Kind.ToString(),
        ProjectName     = projectName,
        Language        = language
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// IPromptFactoryService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Förvandlar ett kodfel till en redo-att-använda AI-instruktion.
/// </summary>
public interface IPromptFactoryService
{
    /// <summary>
    /// Genererar en prompt för ett enskilt issue.
    /// </summary>
    GeneratedPrompt Generate(PromptContext context, PromptTarget target);

    /// <summary>
    /// Genererar prompts för båda targets (Copilot + Claude) i ett anrop.
    /// </summary>
    (GeneratedPrompt Copilot, GeneratedPrompt Claude) GenerateBoth(PromptContext context);

    /// <summary>
    /// Batch-generering: tar en lista av PromptContext och genererar valt target för alla.
    /// Sorteras efter Severity descending.
    /// </summary>
    IReadOnlyList<GeneratedPrompt> GenerateBatch(
        IReadOnlyList<PromptContext> contexts,
        PromptTarget                target);
}
