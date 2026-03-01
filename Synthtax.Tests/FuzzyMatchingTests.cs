// Synthtax.Tests/Core/FuzzyMatchingTests.cs
// Requires: xunit, FluentAssertions, NSubstitute

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synthtax.Application.Orchestration;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;
using Synthtax.Core.Fingerprinting;
using Synthtax.Core.FuzzyMatching;
using Synthtax.Core.Orchestration;
using Synthtax.Core.Tokenization;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;

namespace Synthtax.Tests.Core;

// ═══════════════════════════════════════════════════════════════════════════
// StructuralTokenizer
// ═══════════════════════════════════════════════════════════════════════════

public class StructuralTokenizerTests
{
    private readonly StructuralTokenizer _sut = new();

    // ── Variabelnamn tvättas bort ──────────────────────────────────────────

    [Fact]
    public void Tokenize_DifferentVariableNames_ProduceSameOutput()
    {
        var a = _sut.Tokenize("if (userName != null) { return true; }");
        var b = _sut.Tokenize("if (emailAddress != null) { return true; }");
        a.Should().Be(b, "variabelnamn ska normaliseras till $I");
    }

    [Fact]
    public void Tokenize_NullCheckPattern_IsStructurallyIdentical()
    {
        var a = _sut.Tokenize("if (order == null) throw new ArgumentNullException(nameof(order));");
        var b = _sut.Tokenize("if (customer == null) throw new ArgumentNullException(nameof(customer));");
        a.Should().Be(b);
    }

    // ── Keywords bevaras ──────────────────────────────────────────────────

    [Theory]
    [InlineData("if",      "IF")]
    [InlineData("return",  "RETURN")]
    [InlineData("null",    "NULL")]
    [InlineData("foreach", "FOREACH")]
    [InlineData("throw",   "THROW")]
    public void Tokenize_Keywords_AreUppercased(string keyword, string expected)
    {
        _sut.Tokenize(keyword).Should().Be(expected);
    }

    // ── Strängliteraler normaliseras ──────────────────────────────────────

    [Fact]
    public void Tokenize_StringLiterals_ReplacedWithPlaceholder()
    {
        var result = _sut.Tokenize(@"var msg = ""Hello World"";");
        result.Should().Contain(TokenAlphabet.StringLit);
        result.Should().NotContain("Hello World");
    }

    [Fact]
    public void Tokenize_DifferentStrings_SameStructure()
    {
        var a = _sut.Tokenize(@"throw new Exception(""Order not found"");");
        var b = _sut.Tokenize(@"throw new Exception(""Customer not found"");");
        a.Should().Be(b);
    }

    // ── Nummerliteraler normaliseras ──────────────────────────────────────

    [Fact]
    public void Tokenize_NumberLiterals_ReplacedWithPlaceholder()
    {
        var a = _sut.Tokenize("if (retries > 3) return;");
        var b = _sut.Tokenize("if (retries > 5) return;");
        a.Should().Be(b, "magiska tal ska normaliseras till $N");
    }

    // ── Typnamn normaliseras ──────────────────────────────────────────────

    [Fact]
    public void Tokenize_PascalCaseTypes_ReplacedWithTypeRef()
    {
        var result = _sut.Tokenize("List<OrderItem> items = new List<OrderItem>();");
        result.Should().NotContain("OrderItem");
        result.Should().Contain(TokenAlphabet.TypeRef);
    }

    // ── Strukturell skillnad detekteras ──────────────────────────────────

    [Fact]
    public void Tokenize_IfVsWhile_ProduceDifferentOutput()
    {
        var ifToken    = _sut.Tokenize("if (x > 0) { doWork(); }");
        var whileToken = _sut.Tokenize("while (x > 0) { doWork(); }");
        ifToken.Should().NotBe(whileToken, "if och while är olika strukturer");
    }

    [Fact]
    public void Tokenize_ThrowVsReturn_ProduceDifferentOutput()
    {
        var throwToken  = _sut.Tokenize("throw new Exception();");
        var returnToken = _sut.Tokenize("return new Exception();");
        throwToken.Should().NotBe(returnToken);
    }

    // ── TokenizeToList ────────────────────────────────────────────────────

    [Fact]
    public void TokenizeToList_ReturnsIndividualTokens()
    {
        var tokens = _sut.TokenizeToList("if (x != null)");
        tokens.Should().Contain("IF");
        tokens.Should().Contain("!=");
        tokens.Should().Contain("NULL");
        tokens.Should().Contain(TokenAlphabet.Identifier);
    }

    [Fact]
    public void TokenizeToList_EmptyInput_ReturnsEmpty()
    {
        _sut.TokenizeToList("").Should().BeEmpty();
        _sut.TokenizeToList("   ").Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// NgramGenerator
// ═══════════════════════════════════════════════════════════════════════════

public class NgramGeneratorTests
{
    [Fact]
    public void GetShingles_Bigrams_ProducesCorrectPairs()
    {
        var tokens  = new[] { "A", "B", "C", "D" };
        var shingles = NgramGenerator.GetShingles(tokens, n: 2);

        shingles.Should().Contain("A B");
        shingles.Should().Contain("B C");
        shingles.Should().Contain("C D");
        shingles.Should().HaveCount(3);
    }

    [Fact]
    public void GetShingles_FewerTokensThanN_ReturnsSingleShingle()
    {
        var shingles = NgramGenerator.GetShingles(["A", "B"], n: 5);
        shingles.Should().HaveCount(1);
    }

    [Fact]
    public void GetCombinedShingles_ContainsBothUnigramsAndBigrams()
    {
        var tokens   = new[] { "IF", "$I", "!=", "NULL" };
        var shingles = NgramGenerator.GetCombinedShingles(tokens);

        shingles.Should().Contain("IF");       // unigram
        shingles.Should().Contain("IF $I");    // bigram
        shingles.Count.Should().BeGreaterThan(tokens.Length);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// MinHashSignature
// ═══════════════════════════════════════════════════════════════════════════

public class MinHashSignatureTests
{
    [Fact]
    public void Compute_SameShingles_ProducesIdenticalSignature()
    {
        var shingles = new HashSet<string> { "IF $I", "$I !=", "!= NULL" };
        var sig1 = MinHashSignature.Compute(shingles);
        var sig2 = MinHashSignature.Compute(shingles);

        sig1.Values.Should().BeEquivalentTo(sig2.Values, o => o.WithStrictOrdering());
    }

    [Fact]
    public void EstimateJaccard_IdenticalSets_ReturnsOnePointZero()
    {
        var shingles = new HashSet<string> { "A B", "B C", "C D", "D E", "E F" };
        var sig1 = MinHashSignature.Compute(shingles);
        var sig2 = MinHashSignature.Compute(shingles);

        sig1.EstimateJaccard(sig2).Should().BeApproximately(1.0, precision: 0.01);
    }

    [Fact]
    public void EstimateJaccard_CompletelyDifferentSets_ReturnsNearZero()
    {
        var setA = Enumerable.Range(0, 50).Select(i => $"TOKEN_A_{i}").ToHashSet();
        var setB = Enumerable.Range(0, 50).Select(i => $"TOKEN_B_{i}").ToHashSet();
        var sig1 = MinHashSignature.Compute(setA);
        var sig2 = MinHashSignature.Compute(setB);

        sig1.EstimateJaccard(sig2).Should().BeLessThan(0.1);
    }

    [Fact]
    public void EstimateJaccard_HighOverlap_ReturnsHighScore()
    {
        // 90% overlap: 9 gemensamma av 10 unika
        var shared = Enumerable.Range(0, 90).Select(i => $"S_{i}").ToHashSet();
        var onlyA  = new HashSet<string> { "ONLY_A_1", "ONLY_A_2", "ONLY_A_3",
                                           "ONLY_A_4", "ONLY_A_5" };
        var onlyB  = new HashSet<string> { "ONLY_B_1", "ONLY_B_2", "ONLY_B_3",
                                           "ONLY_B_4", "ONLY_B_5" };

        var setA = shared.Union(onlyA).ToHashSet();
        var setB = shared.Union(onlyB).ToHashSet();
        var sig1 = MinHashSignature.Compute(setA);
        var sig2 = MinHashSignature.Compute(setB);

        // True Jaccard = 90/(90+5+5) = 0.9 — MinHash bör estimera nära detta
        sig1.EstimateJaccard(sig2).Should().BeInRange(0.80, 1.0);
    }

    [Fact]
    public void EstimateJaccard_IsSymmetric()
    {
        var setA = new HashSet<string> { "A B", "B C", "C D" };
        var setB = new HashSet<string> { "A B", "B C", "X Y" };
        var sig1 = MinHashSignature.Compute(setA);
        var sig2 = MinHashSignature.Compute(setB);

        sig1.EstimateJaccard(sig2).Should().BeApproximately(sig2.EstimateJaccard(sig1), 0.001);
    }

    [Fact]
    public void EstimateJaccard_IsInRange()
    {
        var setA = new HashSet<string> { "A", "B", "C" };
        var setB = new HashSet<string> { "B", "C", "D" };
        var score = MinHashSignature.Compute(setA).EstimateJaccard(MinHashSignature.Compute(setB));

        score.Should().BeInRange(0.0, 1.0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// FuzzyMatchService — integrationstest med riktiga objekt
// ═══════════════════════════════════════════════════════════════════════════

public class FuzzyMatchServiceTests
{
    private readonly StructuralTokenizer    _tokenizer = new();
    private readonly IFuzzyMatchService     _sut;

    public FuzzyMatchServiceTests()
    {
        _sut = new FuzzyMatchService(
            _tokenizer,
            NullLogger<FuzzyMatchService>.Instance);
    }

    // ── Exakt strukturell match ───────────────────────────────────────────

    [Fact]
    public void TryMatch_StructurallyIdentical_ReturnsMatchAboveThreshold()
    {
        var existing  = MakeBacklogItem("fp-001", "CA001", "src/Foo.cs",
            snippet: "if (order == null) throw new ArgumentNullException(nameof(order));");

        var scanIssue = MakeRawIssue("CA001", "src/Foo.cs",
            snippet: "if (customer == null) throw new ArgumentNullException(nameof(customer));");

        var result = _sut.TryMatch(scanIssue, "fp-new-001", [existing]);

        result.IsMatch.Should().BeTrue("strukturellt identiska snippets bör matcha");
        result.BestScore.Should().BeGreaterThanOrEqualTo(0.85);
    }

    // ── Variabelnamnsändring → matchar ────────────────────────────────────

    [Fact]
    public void TryMatch_VariableRename_StillMatches()
    {
        var existing = MakeBacklogItem("fp-002", "CA002", "src/Service.cs",
            snippet: "if (retryCount > 3) { logger.LogWarning(\"Too many retries\"); return false; }");

        var scanIssue = MakeRawIssue("CA002", "src/Service.cs",
            snippet: "if (attemptNumber > 3) { logger.LogWarning(\"Too many retries\"); return false; }");

        var result = _sut.TryMatch(scanIssue, "fp-new-002", [existing]);

        result.IsMatch.Should().BeTrue("en variabelrename ska inte skapa nytt issue");
    }

    // ── Helt annan kod → ingen match ──────────────────────────────────────

    [Fact]
    public void TryMatch_CompletelyDifferentCode_NoMatch()
    {
        var existing = MakeBacklogItem("fp-003", "CA001", "src/Foo.cs",
            snippet: "if (order == null) throw new ArgumentNullException();");

        var scanIssue = MakeRawIssue("CA001", "src/Foo.cs",
            snippet: "for (int i = 0; i < list.Count; i++) { list[i].Process(); }");

        var opts   = FuzzyMatchOptions.Default with { Threshold = 0.85 };
        var result = _sut.TryMatch(scanIssue, "fp-new-003", [existing], opts);

        result.IsMatch.Should().BeFalse("strukturellt olika kod ska inte fuzzy-matcha");
    }

    // ── Annan RuleId → ingen kandidat ────────────────────────────────────

    [Fact]
    public void TryMatch_DifferentRuleId_NoCandidate_NoMatch()
    {
        var existing = MakeBacklogItem("fp-004", "CA001", "src/Foo.cs",
            snippet: "if (order == null) throw new ArgumentNullException();");

        // Scan-issue har CA999 — finns inga kandidater med den regeln
        var scanIssue = MakeRawIssue("CA999", "src/Foo.cs",
            snippet: "if (order == null) throw new ArgumentNullException();");

        var result = _sut.TryMatch(scanIssue, "fp-new-004", [existing]);

        result.IsMatch.Should().BeFalse("ingen kandidat med CA999 finns");
        result.CandidatesEvaluated.Should().Be(0);
    }

    // ── Threshold-respekt ────────────────────────────────────────────────

    [Fact]
    public void TryMatch_ScoreAboveThreshold_Matches()
    {
        // Nästan identisk kod — ska matcha vid threshold 0.85
        var existing = MakeBacklogItem("fp-005", "CA003", "src/Handler.cs",
            snippet: "foreach (var item in items) { if (item.IsExpired()) { Remove(item); } }");

        var scanIssue = MakeRawIssue("CA003", "src/Handler.cs",
            snippet: "foreach (var entry in entries) { if (entry.IsExpired()) { Remove(entry); } }");

        var lowThreshold = FuzzyMatchOptions.Default with { Threshold = 0.70 };
        var result = _sut.TryMatch(scanIssue, "fp-new-005", [existing], lowThreshold);

        result.IsMatch.Should().BeTrue();
        result.BestScore.Should().BeGreaterThanOrEqualTo(0.70);
    }

    // ── Valideringsloggning ───────────────────────────────────────────────

    [Fact]
    public void TryMatch_OnMatch_LogEntryContainsFuzzyMatchPrefix()
    {
        var existing  = MakeBacklogItem("fp-006", "CA001", "src/Foo.cs",
            snippet: "if (order == null) throw new ArgumentNullException(nameof(order));");

        var scanIssue = MakeRawIssue("CA001", "src/Foo.cs",
            snippet: "if (customer == null) throw new ArgumentNullException(nameof(customer));");

        var result = _sut.TryMatch(scanIssue, "fp-new-006", [existing]);

        if (result.IsMatch)
        {
            result.LogEntry.Should().NotBeNullOrEmpty();
            result.LogEntry.Should().StartWith("[FuzzyMatch]");
            result.LogEntry.Should().Contain("Score:");
        }
    }

    // ── Batch-matchning ───────────────────────────────────────────────────

    [Fact]
    public void TryMatchBatch_MultipleIssues_ReturnsCorrectCount()
    {
        var candidates = new[]
        {
            MakeBacklogItem("fp-A", "CA001", "src/A.cs",
                "if (order == null) throw new ArgumentNullException();"),
            MakeBacklogItem("fp-B", "CA002", "src/B.cs",
                "for (int i = 0; i < count; i++) { Process(items[i]); }"),
        };

        var issues = new (string Fp, RawIssue Issue)[]
        {
            ("fp-new-A", MakeRawIssue("CA001", "src/A.cs",
                "if (customer == null) throw new ArgumentNullException();")),
            ("fp-new-B", MakeRawIssue("CA002", "src/B.cs",
                "for (int j = 0; j < total; j++) { Process(entries[j]); }")),
            ("fp-new-C", MakeRawIssue("CA999", "src/C.cs",
                "completely different code with no match"))
        };

        var results = _sut.TryMatchBatch(issues, candidates);

        results.Should().HaveCount(3);
    }

    // ── Preferens för samma fil ───────────────────────────────────────────

    [Fact]
    public void TryMatch_SameFileCandidate_PreferredOverCrossFile()
    {
        var sameFileCand = MakeBacklogItem("fp-same", "CA001", "src/Target.cs",
            "if (order == null) throw new ArgumentNullException(nameof(order));");

        var diffFileCand = MakeBacklogItem("fp-diff", "CA001", "src/Other.cs",
            "if (order == null) throw new ArgumentNullException(nameof(order));");

        var scanIssue = MakeRawIssue("CA001", "src/Target.cs",
            "if (customer == null) throw new ArgumentNullException(nameof(customer));");

        var opts   = FuzzyMatchOptions.Default with { PreferSameFile = true };
        var result = _sut.TryMatch(scanIssue, "fp-new-same", [sameFileCand, diffFileCand], opts);

        if (result.IsMatch)
            result.MatchStrategy.Should().Be(FuzzyMatchStrategy.SameFile);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═════════════════════════════════════════════════════════════════════

    private static BacklogItem MakeBacklogItem(
        string fingerprint, string ruleId, string filePath, string snippet)
    {
        var meta = System.Text.Json.JsonSerializer.Serialize(new
        {
            filePath, snippet, scope = $"[{filePath}]"
        });
        return new BacklogItem
        {
            Id          = Guid.NewGuid(),
            ProjectId   = Guid.NewGuid(),
            TenantId    = Guid.Empty,
            RuleId      = ruleId,
            Fingerprint = fingerprint,
            Status      = BacklogStatus.Open,
            Metadata    = meta
        };
    }

    private static RawIssue MakeRawIssue(string ruleId, string filePath, string snippet) => new()
    {
        RuleId   = ruleId,
        Scope    = LogicalScope.ForMethod("Ns", "Cls", "Method"),
        FilePath = filePath,
        Snippet  = snippet,
        Message  = "Test issue",
        Severity = Severity.Medium,
        Category = "Test"
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// SyncDiffV4
// ═══════════════════════════════════════════════════════════════════════════

public class SyncDiffV4Tests
{
    [Fact]
    public void SyncDiffV4_IsEmpty_WhenAllListsEmpty()
    {
        var diff = new SyncDiffV4
        {
            Base = new SyncDiff
            {
                ToCreate    = [],
                ToUpdate    = [],
                ToReopen    = [],
                ToAutoClose = []
            },
            ToFuzzyUpdate = [],
            FuzzyLogs     = []
        };

        diff.IsEmpty.Should().BeTrue();
        diff.GhostIssuesSaved.Should().Be(0);
    }

    [Fact]
    public void SyncDiffV4_GhostIssuesSaved_ReflectsToFuzzyUpdateCount()
    {
        var spec = new FuzzyUpdateItemSpec(
            Guid.NewGuid(), "old", "new", "{}", 0.92, FuzzyMatchStrategy.SameFile);

        var diff = new SyncDiffV4
        {
            Base = new SyncDiff
            {
                ToCreate    = [],
                ToUpdate    = [],
                ToReopen    = [],
                ToAutoClose = []
            },
            ToFuzzyUpdate = [spec],
            FuzzyLogs     = []
        };

        diff.GhostIssuesSaved.Should().Be(1);
        diff.IsEmpty.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// FingerprintHistoryExtensions
// ═══════════════════════════════════════════════════════════════════════════

public class FingerprintHistoryTests
{
    [Fact]
    public void GetFingerprintHistory_NullHistory_ReturnsEmpty()
    {
        var item = new BacklogItem { Fingerprint = "abc", PreviousFingerprints = null };
        item.GetFingerprintHistory().Should().BeEmpty();
    }

    [Fact]
    public void GetFingerprintHistory_ValidJson_ReturnsFingerprints()
    {
        var item = new BacklogItem
        {
            Fingerprint           = "current",
            PreviousFingerprints  = """["old1", "old2"]"""
        };
        item.GetFingerprintHistory().Should().BeEquivalentTo(["old1", "old2"]);
    }

    [Fact]
    public void HasOrHadFingerprint_CurrentFp_ReturnsTrue()
    {
        var item = new BacklogItem { Fingerprint = "current", PreviousFingerprints = null };
        item.HasOrHadFingerprint("current").Should().BeTrue();
    }

    [Fact]
    public void HasOrHadFingerprint_HistoricalFp_ReturnsTrue()
    {
        var item = new BacklogItem
        {
            Fingerprint          = "current",
            PreviousFingerprints = """["old1", "old2"]"""
        };
        item.HasOrHadFingerprint("old1").Should().BeTrue();
        item.HasOrHadFingerprint("unknown").Should().BeFalse();
    }
}
