
using Synthtax.Core.Orchestration;
using Synthtax.Core.Entities;
using Synthtax.Core.Enums;

namespace Synthtax.Application.Orchestration;

/// <summary>
/// Beräknar KPI:er för en <see cref="AnalysisSession"/> efter sync.
///
/// <para><b>OverallScore-formel (0–100, högre = bättre):</b>
/// <code>
///   weightedPenalty = Σ(issue.weight)  för varje aktivt ärende
///   weight(Critical)  = 10
///   weight(High)      = 4
///   weight(Medium)    = 2
///   weight(Low)       = 0.5
///
///   rawScore = 100 - (weightedPenalty / normalizer * 100)
///   score    = Clamp(rawScore, 0, 100)
///
///   normalizer = max(totalIssues * 4, 1)  →  "genomsnittlig High-issue per ärende"
/// </code>
/// Logiken: ett projekt med bara Low-issues straffas lite; ett med Critical-issues straffas hårt.
/// Normalizeringen gör att score inte kraschar mot 0 för stora projekt med mest låg-risk issues.
/// </para>
/// </summary>
public static class KpiCalculator
{
    private static readonly IReadOnlyDictionary<Severity, double> Weights =
        new Dictionary<Severity, double>
        {
            [Severity.Critical] = 10.0,
            [Severity.High]     = 4.0,
            [Severity.Medium]   = 2.0,
            [Severity.Low]      = 0.5
        };

    // ── Publik API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fyller i KPI-fälten på en <see cref="AnalysisSession"/> baserat på sync-resultatet.
    /// </summary>
    /// <param name="session">Sessionen som ska uppdateras (in/out).</param>
    /// <param name="diff">Den beräknade differensen från SyncEngine.</param>
    /// <param name="activeItemsAfterSync">
    /// Alla aktiva BacklogItems för projektet EFTER att sync är klar.
    /// Används för TotalIssues och score-beräkning.
    /// </param>
    public static void Populate(
        AnalysisSession       session,
        SyncDiff              diff,
        IReadOnlyList<ActiveIssueSummary> activeItemsAfterSync)
    {
        session.NewIssues      = diff.NewCount + diff.ToReopen.Count;
        session.ResolvedIssues = diff.AutoCloseCount;
        session.TotalIssues    = activeItemsAfterSync.Count;
        session.OverallScore   = ComputeScore(activeItemsAfterSync);
    }

    /// <summary>
    /// Beräknar OverallScore (0.0–100.0) baserat på aktiva issues och deras svårighetsgrad.
    /// </summary>
    public static double ComputeScore(IReadOnlyList<ActiveIssueSummary> activeIssues)
    {
        if (activeIssues.Count == 0) return 100.0;

        var weightedPenalty = activeIssues.Sum(i =>
            Weights.TryGetValue(i.Severity, out var w) ? w : 2.0);

        // Normalisator: "om alla issues vore High" → raw score degraderar proportionellt
        var normalizer = Math.Max(activeIssues.Count * Weights[Severity.High], 1.0);
        var rawScore   = 100.0 - (weightedPenalty / normalizer * 100.0);

        return Math.Round(Math.Clamp(rawScore, 0.0, 100.0), 2);
    }

    /// <summary>
    /// Beräknar delta-score mellan föregående session och nuvarande.
    /// Positivt = förbättring, negativt = försämring.
    /// </summary>
    public static double ComputeScoreDelta(double previousScore, double currentScore) =>
        Math.Round(currentScore - previousScore, 2);
}

/// <summary>
/// Minimal projektion av ett aktivt BacklogItem för KPI-beräkning.
/// Undviker att ladda hela entiteten när bara severity behövs.
/// </summary>
public sealed record ActiveIssueSummary(
    Guid     BacklogItemId,
    Severity Severity);
