using Synthtax.Core.Enums;

namespace Synthtax.Core.Contracts;

// ═══════════════════════════════════════════════════════════════════════════
// RawIssue
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Universell, språkagnostisk issue-DTO.
/// Alla language-plugins returnerar <see cref="RawIssue"/> — Core refererar
/// aldrig till Roslyn, JDT, tree-sitter eller andra parser-bibliotek.
///
/// <para><b>Ansvarsgräns:</b><br/>
/// Plugin-lagret ansvarar för att sätta <see cref="Scope"/> korrekt.
/// Fingerprinting och persistering är Core/Infrastructure-ansvar.</para>
/// </summary>
public sealed record RawIssue
{
    // ── Identitet ──────────────────────────────────────────────────────────

    /// <summary>Regelns ID, t.ex. "CA001", "JAVA012". Matchar <c>Rule.RuleId</c> i DB.</summary>
    public required string RuleId { get; init; }

    /// <summary>Semantisk plats — primär komponent i fingerprinting.</summary>
    public required LogicalScope Scope { get; init; }

    // ── Plats ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Filsökväg, relativ till projektroten om möjligt.
    /// Normaliseras till forward-slash av <see cref="Synthtax.Core.Normalization.SnippetNormalizer"/>.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>1-baserat startradnummer. Används för display — NOT för fingerprinting.</summary>
    public int StartLine { get; init; }

    /// <summary>1-baserat slutradnummer. Samma som StartLine för enkla issues.</summary>
    public int EndLine { get; init; }

    /// <summary>1-baserad startkolumn. 0 om okänd.</summary>
    public int StartColumn { get; init; }

    // ── Innehåll ──────────────────────────────────────────────────────────

    /// <summary>
    /// Råa kodsnippet från källfilen — INTE normaliserat.
    /// Normalisering sker i FingerprintService innan hashing.
    /// </summary>
    public required string Snippet { get; init; }

    /// <summary>Mänskligt läsbar beskrivning av problemet.</summary>
    public required string Message { get; init; }

    /// <summary>Konkret förslag på åtgärd (en till tre meningar).</summary>
    public string? Suggestion { get; init; }

    // ── Klassificering ─────────────────────────────────────────────────────

    /// <summary>
    /// Regelns standardsvårighetsgrad. Kan åsidosättas av <c>BacklogItem.SeverityOverride</c>.
    /// </summary>
    public required Severity Severity { get; init; }

    /// <summary>Kategori, t.ex. "Null safety", "Concurrency", "Performance".</summary>
    public required string Category { get; init; }

    // ── Auto-fix ──────────────────────────────────────────────────────────

    /// <summary>True om plugin kan generera ett korrekt ersättningssnippet.</summary>
    public bool IsAutoFixable { get; init; }

    /// <summary>
    /// Förslag på ersättningskod. Null om <see cref="IsAutoFixable"/> är false.
    /// Appliceras aldrig automatiskt — kräver alltid mänskligt godkännande.
    /// </summary>
    public string? FixedSnippet { get; init; }

    // ── Metadata ──────────────────────────────────────────────────────────

    /// <summary>
    /// Valfria nyckel-värde-par för plugin-specifik data som inte passar i standardfälten.
    /// Serialiseras till <c>BacklogItem.Metadata</c> (JSON) vid persistering.
    /// Exempel: { "cyclomatic_complexity": "12", "max_allowed": "10" }
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    // ── Builders ──────────────────────────────────────────────────────────

    /// <summary>Returnerar en kopia med uppdaterad svårighetsgrad.</summary>
    public RawIssue WithSeverity(Severity severity) => this with { Severity = severity };

    /// <summary>Returnerar en kopia med auto-fix.</summary>
    public RawIssue WithFix(string fixedSnippet) =>
        this with { IsAutoFixable = true, FixedSnippet = fixedSnippet };

    /// <summary>Läsbar representation för logging/debug.</summary>
    public override string ToString() =>
        $"[{RuleId}] {Severity} @ {FilePath}:{StartLine} — {Scope} — {Message[..Math.Min(80, Message.Length)]}";
}

// ═══════════════════════════════════════════════════════════════════════════
// AnalysisRequest
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Input-kontext som skickas till <see cref="IAnalysisPlugin.AnalyzeAsync"/>.
/// Innehåller allt ett plugin behöver — inga Roslyn-typer, inga filsystem-anrop.
/// </summary>
public sealed record AnalysisRequest
{
    /// <summary>Projektets ID — krävs för fingerprinting.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Filens råinnehåll (UTF-8).</summary>
    public required string FileContent { get; init; }

    /// <summary>
    /// Filsökväg, helst relativ till projektroten.
    /// Används för scope-beräkning och display.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Absolut sökväg till projektroten. Används av FingerprintService för att
    /// göra filsökvägar relativa och plattformsneutrala.
    /// </summary>
    public string? ProjectRootPath { get; init; }

    /// <summary>
    /// Specifika regler att köra. Null = kör alla aktiverade regler för pluginet.
    /// Används för selektiv analys (t.ex. "kör bara säkerhetsregler").
    /// </summary>
    public IReadOnlySet<string>? EnabledRuleIds { get; init; }

    /// <summary>Avbryt analysarbete vid token-signalering.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
}

// ═══════════════════════════════════════════════════════════════════════════
// AnalysisResult
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Output från ett plugin för en enskild fil.</summary>
public sealed record AnalysisResult
{
    public required string            FilePath   { get; init; }
    public required string            Language   { get; init; }
    public required IReadOnlyList<RawIssue> Issues { get; init; }
    public TimeSpan                   Duration   { get; init; }
    public IReadOnlyList<string>      Errors     { get; init; } = [];

    public bool HasIssues => Issues.Count > 0;
    public bool HasErrors => Errors.Count > 0;
}
