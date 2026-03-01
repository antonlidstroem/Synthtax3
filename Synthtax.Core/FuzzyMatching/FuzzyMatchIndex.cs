using System.Text.Json;
using Synthtax.Core.Tokenization;
using Synthtax.Domain.Entities;

namespace Synthtax.Core.FuzzyMatching;

// ═══════════════════════════════════════════════════════════════════════════
// IndexedBacklogItem  —  ett BacklogItem med förberäknad MinHash-signatur
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Wrapper som parar ett BacklogItem med dess förberäknade strukturella signatur.
/// </summary>
internal sealed record IndexedBacklogItem(
    BacklogItem       Item,
    MinHashSignature  Signature,
    string            ExtractedFilePath,
    string            ExtractedScope);

// ═══════════════════════════════════════════════════════════════════════════
// FuzzyMatchIndex
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Förberäknat sökindex över befintliga BacklogItems.
/// Bygger MinHash-signaturer för alla items vid konstruktion och
/// erbjuder effektiv kandidatsökning per (RuleId, filePath) eller (RuleId).
///
/// <para>Konstruktionskomplexitet: O(n × k) där n = items, k = MinHash.K = 128.</para>
/// <para>Sökkomplexitet: O(candidates × k) — kandidatpoolen hålls liten via RuleId-index.</para>
/// </summary>
public sealed class FuzzyMatchIndex
{
    private readonly StructuralTokenizer _tokenizer;

    // Primärt index: RuleId → file-sökväg → lista av indexerade items
    // Möjliggör O(1)-lookup av "items med samma regel i samma fil"
    private readonly Dictionary<string, Dictionary<string, List<IndexedBacklogItem>>>
        _byRuleAndFile;

    // Sekundärt index: RuleId → alla items (för cross-file matching)
    private readonly Dictionary<string, List<IndexedBacklogItem>> _byRule;

    private readonly int _totalItems;

    private FuzzyMatchIndex(
        Dictionary<string, Dictionary<string, List<IndexedBacklogItem>>> byRuleAndFile,
        Dictionary<string, List<IndexedBacklogItem>> byRule,
        int totalItems)
    {
        _byRuleAndFile = byRuleAndFile;
        _byRule        = byRule;
        _totalItems    = totalItems;
    }

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>
    /// Bygger indexet från en samling BacklogItems.
    /// Bör anropas en gång per session med alla relevanta items.
    /// </summary>
    public static FuzzyMatchIndex Build(
        IEnumerable<BacklogItem>  items,
        StructuralTokenizer       tokenizer,
        int                       ngramSize = 2)
    {
        var byRuleAndFile = new Dictionary<string, Dictionary<string, List<IndexedBacklogItem>>>(
            StringComparer.OrdinalIgnoreCase);
        var byRule = new Dictionary<string, List<IndexedBacklogItem>>(
            StringComparer.OrdinalIgnoreCase);

        int count = 0;
        foreach (var item in items)
        {
            var (snippet, filePath, scope) = ExtractFromMetadata(item.Metadata);
            var tokens    = tokenizer.TokenizeToList(snippet);
            var shingles  = NgramGenerator.GetCombinedShingles(tokens);
            var signature = MinHashSignature.Compute(shingles);

            var indexed = new IndexedBacklogItem(item, signature, filePath, scope);

            // Sätt in i RuleId-index
            if (!byRule.TryGetValue(item.RuleId, out var ruleList))
            {
                ruleList = new List<IndexedBacklogItem>();
                byRule[item.RuleId] = ruleList;
            }
            ruleList.Add(indexed);

            // Sätt in i (RuleId, File)-index
            if (!byRuleAndFile.TryGetValue(item.RuleId, out var fileDict))
            {
                fileDict = new Dictionary<string, List<IndexedBacklogItem>>(
                    StringComparer.OrdinalIgnoreCase);
                byRuleAndFile[item.RuleId] = fileDict;
            }
            if (!fileDict.TryGetValue(filePath, out var fileList))
            {
                fileList = new List<IndexedBacklogItem>();
                fileDict[filePath] = fileList;
            }
            fileList.Add(indexed);

            count++;
        }

        return new FuzzyMatchIndex(byRuleAndFile, byRule, count);
    }

    // ── Kandidatsökning ────────────────────────────────────────────────────

    /// <summary>
    /// Hämtar kandidater för ett scan-issue, sorterade efter prioritet:
    /// 1. Samma fil → 2. Alla med samma regel.
    /// </summary>
    public IReadOnlyList<IndexedBacklogItem> GetCandidates(
        string ruleId,
        string filePath,
        bool   preferSameFile,
        int    maxCandidates)
    {
        if (!_byRule.TryGetValue(ruleId, out var allByRule)) return [];

        if (!preferSameFile || string.IsNullOrEmpty(filePath))
            return TakeUpTo(allByRule, maxCandidates);

        // Lägg samma-fil-kandidater FÖRST
        var result = new List<IndexedBacklogItem>(Math.Min(maxCandidates, allByRule.Count));

        if (_byRuleAndFile.TryGetValue(ruleId, out var fileDict) &&
            fileDict.TryGetValue(filePath, out var sameFileCandidates))
        {
            result.AddRange(sameFileCandidates.Take(maxCandidates));
        }

        // Fyll upp med cross-file-kandidater om det finns utrymme
        if (result.Count < maxCandidates)
        {
            var sameFileSet = result.Select(i => i.Item.Id).ToHashSet();
            foreach (var candidate in allByRule)
            {
                if (result.Count >= maxCandidates) break;
                if (!sameFileSet.Contains(candidate.Item.Id))
                    result.Add(candidate);
            }
        }

        return result.AsReadOnly();
    }

    public int TotalItems => _totalItems;

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    /// <summary>
    /// Extraherar snippet, filePath och scope ur BacklogItem.Metadata (JSON).
    /// Faller tillbaka på tomma strängar om metadata saknas eller är skadad.
    /// </summary>
    internal static (string Snippet, string FilePath, string Scope) ExtractFromMetadata(
        string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return (string.Empty, string.Empty, string.Empty);

        try
        {
            var doc  = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;

            var snippet  = root.TryGetProperty("snippet",  out var s) ? s.GetString() ?? "" : "";
            var filePath = root.TryGetProperty("filePath", out var f) ? f.GetString() ?? "" : "";
            var scope    = root.TryGetProperty("scope",    out var sc) ? sc.GetString() ?? "" : "";
            return (snippet, filePath, scope);
        }
        catch { return (string.Empty, string.Empty, string.Empty); }
    }

    private static IReadOnlyList<IndexedBacklogItem> TakeUpTo(
        List<IndexedBacklogItem> list, int max) =>
        max >= list.Count ? list.AsReadOnly() : list.Take(max).ToList().AsReadOnly();
}
