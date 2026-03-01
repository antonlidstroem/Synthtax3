using Microsoft.Extensions.Logging;
using Synthtax.Core.Contracts;
using Synthtax.Core.Tokenization;
using Synthtax.Domain.Entities;

namespace Synthtax.Core.FuzzyMatching;

/// <summary>
/// Implementerar strukturell fuzzy-matchning med MinHash + N-gram Jaccard-similaritet.
///
/// <para><b>Matchningsflöde per issue:</b>
/// <list type="number">
///   <item>Tokenisera scan-snippetet (strukturell normalisering).</item>
///   <item>Beräkna MinHash-signatur för scan-issue.</item>
///   <item>Hämta kandidater från index (samma fil → sedan cross-file, begränsat till MaxCandidates).</item>
///   <item>Beräkna Jaccard-estimat mot varje kandidat via signature.EstimateJaccard().</item>
///   <item>Om bästa kandidat ≥ threshold → returnera match + loggpost.</item>
/// </list>
/// </para>
///
/// <para><b>Valideringsloggning:</b>
/// Alla fuzzy-matchningar loggas i formatet:
/// <c>[FuzzyMatch] (Score: 0.92) RuleId:{ruleId} ScanFP:{fp[..8]}… → ItemId:{id} Strategy:{strategy}</c>
/// </para>
/// </summary>
public sealed class FuzzyMatchService : IFuzzyMatchService
{
    private readonly StructuralTokenizer     _tokenizer;
    private readonly ILogger<FuzzyMatchService> _logger;

    public FuzzyMatchService(
        StructuralTokenizer        tokenizer,
        ILogger<FuzzyMatchService> logger)
    {
        _tokenizer = tokenizer;
        _logger    = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IFuzzyMatchService
    // ═══════════════════════════════════════════════════════════════════════

    /// <inheritdoc/>
    public FuzzyMatchResult TryMatch(
        RawIssue                   scanIssue,
        string                     scanFingerprint,
        IReadOnlyList<BacklogItem> candidates,
        FuzzyMatchOptions?         options = null)
    {
        var opts = options ?? FuzzyMatchOptions.Default;
        if (candidates.Count == 0)
            return FuzzyMatchResult.NoMatch(scanFingerprint, 0.0, 0);

        // Bygg temporärt index för enkelt-issue-sökning
        var index = FuzzyMatchIndex.Build(
            candidates.Take(opts.MaxCandidatesPerIssue),
            _tokenizer,
            opts.NgramSize);

        return MatchSingle(scanIssue, scanFingerprint, index, opts);
    }

    /// <inheritdoc/>
    public IReadOnlyList<FuzzyMatchResult> TryMatchBatch(
        IReadOnlyList<(string Fingerprint, RawIssue Issue)> unmatchedIssues,
        IReadOnlyList<BacklogItem>                          candidates,
        FuzzyMatchOptions?                                  options = null)
    {
        var opts = options ?? FuzzyMatchOptions.Default;
        if (unmatchedIssues.Count == 0 || candidates.Count == 0)
            return unmatchedIssues.Select(i =>
                FuzzyMatchResult.NoMatch(i.Fingerprint, 0.0, 0)).ToList().AsReadOnly();

        // Bygg indexet en gång för hela batchen — det är det kritiska för prestanda
        var index = FuzzyMatchIndex.Build(candidates, _tokenizer, opts.NgramSize);
        _logger.LogDebug(
            "FuzzyMatch: Index byggt med {Items} items för {Issues} unmatched issues.",
            candidates.Count, unmatchedIssues.Count);

        var results = new List<FuzzyMatchResult>(unmatchedIssues.Count);
        foreach (var (fingerprint, issue) in unmatchedIssues)
            results.Add(MatchSingle(issue, fingerprint, index, opts));

        var matchCount = results.Count(r => r.IsMatch);
        _logger.LogInformation(
            "FuzzyMatch batch klar: {Matched}/{Total} issues fick fuzzy-match (threshold={Threshold:F2}).",
            matchCount, unmatchedIssues.Count, opts.Threshold);

        return results.AsReadOnly();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Kärn-matchningslogik
    // ═══════════════════════════════════════════════════════════════════════

    private FuzzyMatchResult MatchSingle(
        RawIssue          scanIssue,
        string            scanFingerprint,
        FuzzyMatchIndex   index,
        FuzzyMatchOptions opts)
    {
        // ── Beräkna scan-signatur ─────────────────────────────────────────
        var scanTokens    = _tokenizer.TokenizeToList(scanIssue.Snippet,
                                Path.GetExtension(scanIssue.FilePath));
        var scanShingles  = NgramGenerator.GetCombinedShingles(scanTokens);
        var scanSignature = MinHashSignature.Compute(scanShingles);

        // ── Hämta kandidater ──────────────────────────────────────────────
        var candidates = index.GetCandidates(
            ruleId:          scanIssue.RuleId,
            filePath:        scanIssue.FilePath,
            preferSameFile:  opts.PreferSameFile,
            maxCandidates:   opts.MaxCandidatesPerIssue);

        if (candidates.Count == 0)
            return FuzzyMatchResult.NoMatch(scanFingerprint, 0.0, 0);

        // ── Score alla kandidater ─────────────────────────────────────────
        double bestScore           = 0.0;
        IndexedBacklogItem? bestCandidate = null;

        foreach (var candidate in candidates)
        {
            var score = scanSignature.EstimateJaccard(candidate.Signature);
            if (score > bestScore)
            {
                bestScore     = score;
                bestCandidate = candidate;
            }
        }

        // ── Tröskel-check ─────────────────────────────────────────────────
        if (bestScore < opts.Threshold || bestCandidate is null)
        {
            _logger.LogDebug(
                "[FuzzyMatch] NoMatch — RuleId:{RuleId} ScanFP:{FP}… BestScore:{Score:F3} < {Threshold:F2}",
                scanIssue.RuleId,
                scanFingerprint[..Math.Min(8, scanFingerprint.Length)],
                bestScore,
                opts.Threshold);

            return FuzzyMatchResult.NoMatch(scanFingerprint, bestScore, candidates.Count);
        }

        // ── Match hittad ──────────────────────────────────────────────────
        var strategy = DetermineStrategy(scanIssue.FilePath, bestCandidate.ExtractedFilePath);
        var fpShort  = scanFingerprint[..Math.Min(8, scanFingerprint.Length)];
        var logEntry = $"[FuzzyMatch] (Score: {bestScore:F2}) " +
                       $"RuleId:{scanIssue.RuleId} " +
                       $"ScanFP:{fpShort}… → " +
                       $"ItemId:{bestCandidate.Item.Id:N[..8]} " +
                       $"Strategy:{strategy}";

        // Valideringsloggning per kravspecifikationen
        _logger.LogInformation("{LogEntry}", logEntry);

        return new FuzzyMatchResult
        {
            ScanFingerprint     = scanFingerprint,
            MatchedItem         = bestCandidate.Item,
            BestScore           = bestScore,
            CandidatesEvaluated = candidates.Count,
            MatchStrategy       = strategy,
            LogEntry            = logEntry
        };
    }

    private static FuzzyMatchStrategy DetermineStrategy(
        string scanFilePath, string candidateFilePath) =>
        string.Equals(scanFilePath, candidateFilePath, StringComparison.OrdinalIgnoreCase)
            ? FuzzyMatchStrategy.SameFile
            : FuzzyMatchStrategy.SameRule;
}
