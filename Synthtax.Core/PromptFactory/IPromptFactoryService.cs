using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;
using Synthtax.Domain.Entities;

namespace Synthtax.Core.PromptFactory;

// ═══════════════════════════════════════════════════════════════════════════
// PromptTarget  —  vilket AI-verktyg prompten är optimerad för
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Väljer vilket format och vilken detaljnivå som genereras.
/// </summary>
public enum PromptTarget
{
    /// <summary>
    /// GitHub Copilot / Copilot Chat i editorn.
    /// Format: kort, actionabelt, inga långa introduktioner.
    /// Max ~300 tokens — inline kommentarsformat, kodfokuserat.
    /// </summary>
    Copilot = 0,

    /// <summary>
    /// Claude (Anthropic) — fullständig teknisk spec.
    /// Format: strukturerat med rubriker, arkitektonisk kontext, kravlista.
    /// Optimerat för Claude's "Technical Spec"-format med XML-taggar.
    /// </summary>
    Claude = 1,

    /// <summary>
    /// Generellt format — passar de flesta chat-AI:er.
    /// Mellanläge: tydligare än Copilot, kortare än Claude.
    /// </summary>
    General = 2
}

// ═══════════════════════════════════════════════════════════════════════════
// PromptRequest  —  input till PromptFactoryService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Allt som PromptFactory behöver för att bygga en kontextuellt rik prompt.
/// </summary>
public sealed record PromptRequest
{
    // ── Obligatoriskt ──────────────────────────────────────────────────────

    /// <summary>Målet för prompten — styr format och detaljnivå.</summary>
    public required PromptTarget Target { get; init; }

    /// <summary>Det issue som prompten ska lösa.</summary>
    public required RawIssue Issue { get; init; }

    // ── Valfri arkitektonisk kontext ──────────────────────────────────────

    /// <summary>
    /// Projektets primärspråk och ramverk, t.ex. "C# 12 / .NET 8 / ASP.NET Core".
    /// Inkluderas i Claude-prompts för att ge precist teknikkontext.
    /// </summary>
    public string? ProjectTechStack { get; init; }

    /// <summary>
    /// Projektets arkitektoniska mönster, t.ex. "Clean Architecture med CQRS".
    /// Hjälper Claude att generera lösningar som följer befintliga mönster.
    /// </summary>
    public string? ArchitecturePattern { get; init; }

    /// <summary>
    /// Ytterligare kodfiler som är relevanta för att förstå kontexten.
    /// Inkluderas i Claude-prompts som <c>&lt;related_files&gt;</c>-block.
    /// Håll till max 3 filer för att undvika tokengränser.
    /// </summary>
    public IReadOnlyList<RelatedFile>? RelatedFiles { get; init; }

    /// <summary>
    /// Befintlig BacklogItem om detta är en uppföljning (t.ex. för en åtgärd).
    /// Ger historisk kontext: skapad-datum, status, kommentarer.
    /// </summary>
    public BacklogItem? ExistingBacklogItem { get; init; }

    /// <summary>
    /// Kodstil-preferenser, t.ex. "Undvik var, föredra explicit typdeklaration".
    /// </summary>
    public string? CodingConventions { get; init; }
}

/// <summary>En relaterad kodfil inkluderad som kontext i prompten.</summary>
public sealed record RelatedFile(
    string FilePath,
    string Content,
    string? Description = null);

// ═══════════════════════════════════════════════════════════════════════════
// PromptResult  —  output från PromptFactoryService
// ═══════════════════════════════════════════════════════════════════════════

public sealed record PromptResult
{
    /// <summary>Den genererade prompten, klar att klistra in i AI-verktyget.</summary>
    public required string PromptText { get; init; }

    /// <summary>Uppskattad tokenlängd (approximation: tecken / 4).</summary>
    public int EstimatedTokens => PromptText.Length / 4;

    /// <summary>Vilket target prompten är optimerad för.</summary>
    public required PromptTarget Target { get; init; }

    /// <summary>RuleId som prompten adresserar.</summary>
    public required string RuleId { get; init; }

    /// <summary>Tidpunkt för generering (UTC).</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Rekommenderade taggar/kommandon att använda med prompten.
    /// T.ex. ["/fix", "@workspace"] för Copilot, eller tomma för Claude.
    /// </summary>
    public IReadOnlyList<string> SuggestedCommands { get; init; } = [];
}

// ═══════════════════════════════════════════════════════════════════════════
// IPromptFactoryService
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Förvandlar en <see cref="RawIssue"/> till en AI-optimerad prompt.
///
/// <para><b>Targets:</b>
/// <list type="table">
///   <listheader><term>Target</term><term>Längd</term><term>Format</term><term>Fokus</term></listheader>
///   <item><term>Copilot</term> <term>~200tok</term><term>Inline</term><term>Kod-fix direkt</term></item>
///   <item><term>Claude</term>  <term>~800tok</term><term>Spec</term>  <term>Arkitektur + krav</term></item>
///   <item><term>General</term> <term>~400tok</term><term>Mixed</term> <term>Balanserat</term></item>
/// </list>
/// </para>
/// </summary>
public interface IPromptFactoryService
{
    /// <summary>Genererar en prompt för ett enskilt issue.</summary>
    PromptResult Generate(PromptRequest request);

    /// <summary>
    /// Genererar prompts för en lista av issues i en batch.
    /// Optimalt när man vill processa en hel BacklogItem-sida.
    /// </summary>
    IReadOnlyList<PromptResult> GenerateBatch(
        IReadOnlyList<RawIssue> issues,
        PromptTarget            target,
        string?                 projectTechStack = null);
}
