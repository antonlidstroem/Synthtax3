// Synthtax.Tests/Application/SyncEngineTests.cs
// Requires: xunit, FluentAssertions, NSubstitute

using FluentAssertions;
using NSubstitute;
using Synthtax.Application.Orchestration;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;
using Synthtax.Core.Fingerprinting;
using Synthtax.Core.Orchestration;
using Synthtax.Domain.Entities;
using Synthtax.Domain.Enums;

namespace Synthtax.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// SyncEngine — enhetstester
// ═══════════════════════════════════════════════════════════════════════════

public class SyncEngineTests
{
    private static readonly Guid ProjectId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // SyncEngine använder IFingerprintService — vi mockar den för att
    // ha fullständig kontroll över fingerprints i testerna.
    private readonly IFingerprintService _mockFp;
    private readonly SyncEngine          _sut;

    public SyncEngineTests()
    {
        _mockFp = Substitute.For<IFingerprintService>();
        _sut    = new SyncEngine(_mockFp);
    }

    // ── Nytt ärende ────────────────────────────────────────────────────────

    [Fact]
    public void Compute_NewIssue_NotInDb_GoesToCreate()
    {
        var issue = MakeIssue("CA001");
        SetupFingerprints(["fp-001"]);

        var diff = _sut.Compute(ProjectId, [issue], existing: []);

        diff.ToCreate.Should().HaveCount(1);
        diff.ToCreate[0].Fingerprint.Should().Be("fp-001");
        diff.ToCreate[0].RuleId.Should().Be("CA001");
        diff.ToUpdate.Should().BeEmpty();
        diff.ToAutoClose.Should().BeEmpty();
        diff.ToReopen.Should().BeEmpty();
    }

    // ── Exakt match ────────────────────────────────────────────────────────

    [Fact]
    public void Compute_MatchedActiveIssue_GoesToUpdate()
    {
        var issue    = MakeIssue("CA001");
        var existing = MakeExistingItem("fp-001", BacklogStatus.Open, autoClose: false);
        SetupFingerprints(["fp-001"]);

        var diff = _sut.Compute(ProjectId, [issue], existing: D("fp-001", existing));

        diff.ToUpdate.Should().HaveCount(1);
        diff.ToUpdate[0].BacklogItemId.Should().Be(existing.Id);
        diff.ToCreate.Should().BeEmpty();
        diff.ToAutoClose.Should().BeEmpty();
        diff.ToReopen.Should().BeEmpty();
    }

    [Fact]
    public void Compute_MatchedAcknowledgedIssue_GoesToUpdate_NotReopen()
    {
        var issue    = MakeIssue("CA001");
        var existing = MakeExistingItem("fp-001", BacklogStatus.Acknowledged, autoClose: false);
        SetupFingerprints(["fp-001"]);

        var diff = _sut.Compute(ProjectId, [issue], existing: D("fp-001", existing));

        diff.ToUpdate.Should().HaveCount(1);
        diff.ToReopen.Should().BeEmpty();
    }

    // ── Re-open ────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_AutoClosedResolvedIssue_FoundAgain_GoesToReopen()
    {
        var issue    = MakeIssue("CA001");
        var existing = MakeExistingItem("fp-001", BacklogStatus.Resolved, autoClose: true);
        SetupFingerprints(["fp-001"]);

        var diff = _sut.Compute(ProjectId, [issue], existing: D("fp-001", existing));

        diff.ToReopen.Should().HaveCount(1);
        diff.ToReopen[0].BacklogItemId.Should().Be(existing.Id);
        diff.ToUpdate.Should().BeEmpty();
        diff.ToCreate.Should().BeEmpty();
    }

    [Fact]
    public void Compute_ManuallyResolvedIssue_FoundAgain_GoesToUpdate_NotReopen()
    {
        // Manuellt stängt (AutoClosed=false) ska INTE återöppnas automatiskt
        var issue    = MakeIssue("CA001");
        var existing = MakeExistingItem("fp-001", BacklogStatus.Resolved, autoClose: false);
        SetupFingerprints(["fp-001"]);

        var diff = _sut.Compute(ProjectId, [issue], existing: D("fp-001", existing));

        // Resolved med AutoClosed=false är "terminal" för auto-logiken
        diff.ToReopen.Should().BeEmpty();
        diff.ToUpdate.Should().BeEmpty();  // Resolved är inte "Active" → ignoreras
        diff.ToCreate.Should().BeEmpty();
    }

    // ── Auto-close ─────────────────────────────────────────────────────────

    [Fact]
    public void Compute_ActiveIssue_MissingFromScan_GoesToAutoClose()
    {
        var existing = MakeExistingItem("fp-001", BacklogStatus.Open, autoClose: false);
        SetupFingerprints([]);  // inga inkommande issues

        var diff = _sut.Compute(ProjectId, [], existing: D("fp-001", existing));

        diff.ToAutoClose.Should().HaveCount(1);
        diff.ToAutoClose[0].BacklogItemId.Should().Be(existing.Id);
    }

    [Theory]
    [InlineData(BacklogStatus.Accepted)]
    [InlineData(BacklogStatus.FalsePositive)]
    public void Compute_TerminalIssue_MissingFromScan_NotAutoClose(BacklogStatus terminalStatus)
    {
        // Accepted och FalsePositive är mänskliga beslut — respekteras alltid
        var existing = MakeExistingItem("fp-001", terminalStatus, autoClose: false);
        SetupFingerprints([]);

        var diff = _sut.Compute(ProjectId, [], existing: D("fp-001", existing));

        diff.ToAutoClose.Should().BeEmpty();
        diff.IsEmpty.Should().BeTrue();
    }

    // ── Blandad scenario ───────────────────────────────────────────────────

    [Fact]
    public void Compute_MixedScenario_CorrectlyClassifiesAll()
    {
        // Scan hittar: fp-new, fp-match, fp-reopen
        // DB har: fp-match (Open), fp-reopen (Resolved+AutoClosed), fp-close (InProgress)

        var issueNew    = MakeIssue("CA001");
        var issueMatch  = MakeIssue("CA002");
        var issueReopen = MakeIssue("CA003");

        var existMatch  = MakeExistingItem("fp-match",  BacklogStatus.Open,     autoClose: false);
        var existReopen = MakeExistingItem("fp-reopen", BacklogStatus.Resolved, autoClose: true);
        var existClose  = MakeExistingItem("fp-close",  BacklogStatus.InProgress, autoClose: false);

        // Fingerprints i samma ordning som issues-listan
        SetupFingerprints(["fp-new", "fp-match", "fp-reopen"]);

        var existing = new Dictionary<string, BacklogItem>
        {
            ["fp-match"]  = existMatch,
            ["fp-reopen"] = existReopen,
            ["fp-close"]  = existClose
        };

        var diff = _sut.Compute(ProjectId,
            [issueNew, issueMatch, issueReopen],
            existing);

        diff.ToCreate.Should().HaveCount(1)
            .And.Contain(x => x.Fingerprint == "fp-new");
        diff.ToUpdate.Should().HaveCount(1)
            .And.Contain(x => x.BacklogItemId == existMatch.Id);
        diff.ToReopen.Should().HaveCount(1)
            .And.Contain(x => x.BacklogItemId == existReopen.Id);
        diff.ToAutoClose.Should().HaveCount(1)
            .And.Contain(x => x.BacklogItemId == existClose.Id);
    }

    // ── Tom scan ───────────────────────────────────────────────────────────

    [Fact]
    public void Compute_EmptyScan_AllActiveItemsAutoClose()
    {
        var items = Enumerable.Range(1, 5).Select(i =>
            MakeExistingItem($"fp-{i:D3}", BacklogStatus.Open, autoClose: false)).ToList();

        SetupFingerprints([]);
        var existing = items.ToDictionary(i => i.Fingerprint);

        var diff = _sut.Compute(ProjectId, [], existing);

        diff.ToAutoClose.Should().HaveCount(5);
        diff.ToCreate.Should().BeEmpty();
    }

    // ── SyncDiff summary properties ────────────────────────────────────────

    [Fact]
    public void SyncDiff_IsEmpty_TrueWhenNothingToDo()
    {
        SetupFingerprints([]);
        var diff = _sut.Compute(ProjectId, [], []);
        diff.IsEmpty.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private void SetupFingerprints(IReadOnlyList<string> hashes)
    {
        _mockFp.ComputeBatch(Arg.Any<IReadOnlyList<FingerprintInput>>())
               .Returns(hashes);
    }

    private static RawIssue MakeIssue(string ruleId) => new()
    {
        RuleId   = ruleId,
        Scope    = LogicalScope.ForMethod("Ns", "Cls", "Method"),
        FilePath = "src/Foo.cs",
        Snippet  = "var x = null;",
        Message  = "Null-issue",
        Severity = Severity.Medium,
        Category = "Null safety"
    };

    private static BacklogItem MakeExistingItem(
        string fingerprint, BacklogStatus status, bool autoClose) => new()
    {
        Id          = Guid.NewGuid(),
        ProjectId   = ProjectId,
        TenantId    = Guid.Empty,
        RuleId      = "CA001",
        Fingerprint = fingerprint,
        Status      = status,
        AutoClosed  = autoClose,
        Metadata    = "{}"
    };

    private static Dictionary<string, BacklogItem> D(string fp, BacklogItem item) =>
        new() { [fp] = item };
}

// ═══════════════════════════════════════════════════════════════════════════
// KpiCalculator — enhetstester
// ═══════════════════════════════════════════════════════════════════════════

public class KpiCalculatorTests
{
    private static SyncDiff EmptyDiff => new()
    {
        ToCreate    = [],
        ToUpdate    = [],
        ToReopen    = [],
        ToAutoClose = []
    };

    private static SyncDiff MakeDiff(int newCount, int autoCloseCount, int reopenCount) =>
        new()
        {
            ToCreate    = Enumerable.Range(0, newCount)
                .Select(_ => new NewItemSpec(Guid.NewGuid().ToString("N"), "CA001", "{}", Severity.Medium))
                .ToList().AsReadOnly(),
            ToUpdate    = [],
            ToReopen    = Enumerable.Range(0, reopenCount)
                .Select(_ => new ReopenItemSpec(Guid.NewGuid(), "{}"))
                .ToList().AsReadOnly(),
            ToAutoClose = Enumerable.Range(0, autoCloseCount)
                .Select(_ => new AutoCloseItemSpec(Guid.NewGuid()))
                .ToList().AsReadOnly()
        };

    // ── KPI-populering ────────────────────────────────────────────────────

    [Fact]
    public void Populate_SetsNewIssues_AsNewPlusReopen()
    {
        var diff    = MakeDiff(newCount: 3, autoCloseCount: 1, reopenCount: 2);
        var session = new AnalysisSession { Id = Guid.NewGuid(), ProjectId = Guid.NewGuid() };

        KpiCalculator.Populate(session, diff, activeAfterSync: []);

        session.NewIssues.Should().Be(5,      "3 nya + 2 återöppnade");
        session.ResolvedIssues.Should().Be(1, "1 auto-stängd");
        session.TotalIssues.Should().Be(0,    "inga aktiva issues efter sync");
    }

    // ── Score-beräkning ───────────────────────────────────────────────────

    [Fact]
    public void ComputeScore_NoIssues_Returns100()
    {
        KpiCalculator.ComputeScore([]).Should().Be(100.0);
    }

    [Fact]
    public void ComputeScore_AllCritical_LowScore()
    {
        var issues = Enumerable.Range(0, 10)
            .Select(_ => new ActiveIssueSummary(Guid.NewGuid(), Severity.Critical))
            .ToList();

        var score = KpiCalculator.ComputeScore(issues);

        // 10 Critical (weight=10 var) vs normalizer (10 items × High-weight=4 = 40)
        // penalty=100, normalizer=40, rawScore=100-(100/40*100)=-150 → clamped to 0
        score.Should().Be(0.0, "10 Critical issues ger 0 i score");
    }

    [Fact]
    public void ComputeScore_AllLow_HighScore()
    {
        var issues = Enumerable.Range(0, 5)
            .Select(_ => new ActiveIssueSummary(Guid.NewGuid(), Severity.Low))
            .ToList();

        var score = KpiCalculator.ComputeScore(issues);

        // penalty=5*0.5=2.5, normalizer=5*4=20, rawScore=100-(2.5/20*100)=87.5
        score.Should().Be(87.5);
    }

    [Fact]
    public void ComputeScore_Mixed_IsInRange()
    {
        var issues = new List<ActiveIssueSummary>
        {
            new(Guid.NewGuid(), Severity.High),
            new(Guid.NewGuid(), Severity.Medium),
            new(Guid.NewGuid(), Severity.Low),
            new(Guid.NewGuid(), Severity.Low),
        };

        var score = KpiCalculator.ComputeScore(issues);

        score.Should().BeInRange(0, 100);
        score.Should().BeGreaterThan(50.0, "blandat med mest låg-risk bör ge > 50");
    }

    [Fact]
    public void ComputeScore_IsAlwaysClamped0To100()
    {
        // Extremt scenario — score ska aldrig gå utanför [0, 100]
        var massiveCritical = Enumerable.Range(0, 1000)
            .Select(_ => new ActiveIssueSummary(Guid.NewGuid(), Severity.Critical))
            .ToList();

        KpiCalculator.ComputeScore(massiveCritical).Should().BeGreaterThanOrEqualTo(0.0);
        KpiCalculator.ComputeScore([]).Should().BeLessThanOrEqualTo(100.0);
    }

    // ── ScoreDelta ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(60.0, 75.0, 15.0)]   // förbättring
    [InlineData(75.0, 60.0, -15.0)]  // försämring
    [InlineData(100.0, 100.0, 0.0)]  // ingen förändring
    public void ComputeScoreDelta_IsCorrect(double prev, double curr, double expected)
    {
        KpiCalculator.ComputeScoreDelta(prev, curr).Should().Be(expected);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// BacklogItemStatus — enhetstester
// ═══════════════════════════════════════════════════════════════════════════

public class BacklogItemStatusTests
{
    [Theory]
    [InlineData(BacklogStatus.Open,         true)]
    [InlineData(BacklogStatus.Acknowledged, true)]
    [InlineData(BacklogStatus.InProgress,   true)]
    [InlineData(BacklogStatus.Resolved,     false)]
    [InlineData(BacklogStatus.Accepted,     false)]
    [InlineData(BacklogStatus.FalsePositive, false)]
    public void IsActive_CorrectForAllStatuses(BacklogStatus status, bool expected)
    {
        BacklogItemStatus.IsActive(status).Should().Be(expected);
    }

    [Theory]
    [InlineData(BacklogStatus.Resolved,    true,  true)]   // auto-stängd → kan återöppnas
    [InlineData(BacklogStatus.Resolved,    false, false)]  // manuellt stängd → respekteras
    [InlineData(BacklogStatus.Open,        true,  false)]  // aktiv → inget behov
    [InlineData(BacklogStatus.Accepted,    true,  false)]  // terminal → aldrig
    public void ShouldReopen_CorrectLogic(BacklogStatus status, bool autoClosedFlag, bool expected)
    {
        BacklogItemStatus.ShouldReopen(status, autoClosedFlag).Should().Be(expected);
    }
}
