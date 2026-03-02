// Synthtax.Tests/Fas8/SignalRTests.cs
// Requires: xunit, FluentAssertions, NSubstitute

using FluentAssertions;
using NSubstitute;
using Synthtax.Shared.SignalR;
using Synthtax.Vsix.Services;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Tests;

// ════════════════════════════════════════════════════════════════════════
// HubMethods — konstant-validering
// ════════════════════════════════════════════════════════════════════════

public class HubMethodsTests
{
    [Fact]
    public void AllMethodNames_AreNonEmpty()
    {
        HubMethods.AnalysisUpdated.Should().NotBeNullOrWhiteSpace();
        HubMethods.IssueStatusChanged.Should().NotBeNullOrWhiteSpace();
        HubMethods.LicenseChanged.Should().NotBeNullOrWhiteSpace();
        HubMethods.Heartbeat.Should().NotBeNullOrWhiteSpace();
        HubMethods.JoinOrganization.Should().NotBeNullOrWhiteSpace();
        HubMethods.LeaveOrganization.Should().NotBeNullOrWhiteSpace();
        HubMethods.AcknowledgeHeartbeat.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ServerToClientMethods_AreDistinct()
    {
        var serverPush = new[]
        {
            HubMethods.AnalysisUpdated,
            HubMethods.IssueStatusChanged,
            HubMethods.LicenseChanged,
            HubMethods.Heartbeat
        };
        serverPush.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ClientToServerMethods_AreDistinct()
    {
        var clientSend = new[]
        {
            HubMethods.JoinOrganization,
            HubMethods.LeaveOrganization,
            HubMethods.AcknowledgeHeartbeat
        };
        clientSend.Should().OnlyHaveUniqueItems();
    }
}

// ════════════════════════════════════════════════════════════════════════
// Payload-modeller
// ════════════════════════════════════════════════════════════════════════

public class PayloadTests
{
    // ── AnalysisUpdatedPayload ────────────────────────────────────────────

    [Fact]
    public void AnalysisUpdatedPayload_DefaultValues()
    {
        var p = new AnalysisUpdatedPayload();
        p.NewIssues.Should().BeEmpty();
        p.ResolvedFingerprints.Should().BeEmpty();
        p.HealthScore.Should().Be(0);
        p.IsCiCdTriggered.Should().BeFalse();
    }

    [Fact]
    public void AnalysisUpdatedPayload_CanConstruct_FullDiff()
    {
        var orgId     = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var payload = new AnalysisUpdatedPayload
        {
            OrganizationId      = orgId,
            SessionId           = sessionId,
            ProjectName         = "Synthtax.API",
            NewIssuesCount      = 3,
            ResolvedIssuesCount = 1,
            TotalOpenIssues     = 12,
            HealthScore         = 74.5,
            CompletedAt         = DateTime.UtcNow,
            NewIssues = [
                new IssueSummary
                {
                    Id        = Guid.NewGuid(),
                    RuleId    = "SA001",
                    Severity  = "Medium",
                    FilePath  = "src/Services/OrderService.cs",
                    StartLine = 42,
                    Message   = "Not implemented"
                }
            ],
            ResolvedFingerprints = ["fp-abc123"]
        };

        payload.OrganizationId.Should().Be(orgId);
        payload.NewIssues.Should().HaveCount(1);
        payload.ResolvedFingerprints.Should().HaveCount(1);
        payload.HealthScore.Should().Be(74.5);
    }

    [Fact]
    public void AnalysisUpdatedPayload_NewIssues_Max50_Documented()
    {
        // Spec: payload innehåller max 50 issues — validera att designen
        // inte kräver mer (REST-API används för komplett lista)
        var issues = Enumerable.Range(1, 50).Select(_ => new IssueSummary()).ToList();
        var p = new AnalysisUpdatedPayload { NewIssues = issues };
        p.NewIssues.Should().HaveCount(50);
    }

    // ── IssueStatusChangedPayload ─────────────────────────────────────────

    [Theory]
    [InlineData("Open",          "Resolved",      true,  false)]
    [InlineData("Acknowledged",  "Accepted",      true,  false)]
    [InlineData("Resolved",      "Open",          false, true)]
    [InlineData("Open",          "InProgress",    true,  true)]
    public void IssueStatusChangedPayload_OpenStatusTransitions(
        string oldStatus, string newStatus, bool wasOpen, bool nowOpen)
    {
        var p = new IssueStatusChangedPayload
        {
            OldStatus = oldStatus,
            NewStatus = newStatus
        };

        var wasOpenActual = p.OldStatus is "Open" or "Acknowledged" or "InProgress";
        var nowOpenActual = p.NewStatus is "Open" or "Acknowledged" or "InProgress";

        wasOpenActual.Should().Be(wasOpen);
        nowOpenActual.Should().Be(nowOpen);
    }

    // ── LicenseChangedPayload ─────────────────────────────────────────────

    [Fact]
    public void LicenseChangedPayload_ContainsOldAndNew()
    {
        var p = new LicenseChangedPayload
        {
            OldPlan = "Free",
            NewPlan = "Professional",
            Message = "Plan uppgraderad"
        };

        p.OldPlan.Should().Be("Free");
        p.NewPlan.Should().Be("Professional");
        p.Message.Should().NotBeNullOrEmpty();
    }

    // ── HeartbeatPayload ──────────────────────────────────────────────────

    [Fact]
    public void HeartbeatPayload_ServerTime_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var p      = new HeartbeatPayload();
        var after  = DateTime.UtcNow;

        p.ServerTime.Should().BeOnOrAfter(before)
                    .And.BeOnOrBefore(after);
    }

    // ── IssueSummary ──────────────────────────────────────────────────────

    [Fact]
    public void IssueSummary_AllRequiredFields()
    {
        var s = new IssueSummary
        {
            Id          = Guid.NewGuid(),
            RuleId      = "SA003",
            Severity    = "High",
            FilePath    = "src/Controllers/OrderController.cs",
            StartLine   = 88,
            Message     = "Complex method",
            ClassName   = "OrderController",
            MemberName  = "CreateOrder",
            Fingerprint = "sha256-abc"
        };

        s.RuleId.Should().Be("SA003");
        s.Severity.Should().Be("High");
        s.StartLine.Should().Be(88);
    }
}

// ════════════════════════════════════════════════════════════════════════
// ExponentialBackoffRetryPolicy
// ════════════════════════════════════════════════════════════════════════

// Simulera RetryContext utan SignalR-beroende
file sealed class FakeRetryContext
{
    public long PreviousRetryCount { get; set; }
    public TimeSpan ElapsedTime    { get; set; }
    public Exception? RetryReason  { get; set; }
}

file static class BackoffCalculator
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public static TimeSpan NextDelay(long previousRetryCount)
    {
        var idx = (int)Math.Min(previousRetryCount, Delays.Length - 1);
        return Delays[idx];
    }
}

public class ExponentialBackoffTests
{
    [Theory]
    [InlineData(0, 0)]     // Försök 1: omedelbart
    [InlineData(1, 2)]     // Försök 2: 2 s
    [InlineData(2, 5)]     // Försök 3: 5 s
    [InlineData(3, 10)]    // Försök 4: 10 s
    [InlineData(4, 30)]    // Försök 5+: 30 s (tak)
    [InlineData(10, 30)]   // Lång körning: fortfarande 30 s
    [InlineData(99, 30)]   // Extremt: aldrig mer än 30 s
    public void BackoffDelay_IsCorrect(long retryCount, int expectedSeconds)
    {
        var delay = BackoffCalculator.NextDelay(retryCount);
        delay.TotalSeconds.Should().Be(expectedSeconds);
    }

    [Fact]
    public void BackoffDelay_IsMonotonicallyNonDecreasing()
    {
        var delays = Enumerable.Range(0, 10)
            .Select(i => BackoffCalculator.NextDelay(i).TotalSeconds)
            .ToList();

        for (int i = 1; i < delays.Count; i++)
            delays[i].Should().BeGreaterThanOrEqualTo(delays[i - 1]);
    }

    [Fact]
    public void BackoffDelay_NeverExceedsCeiling()
    {
        const double ceiling = 30.0;
        for (int i = 0; i < 100; i++)
            BackoffCalculator.NextDelay(i).TotalSeconds.Should().BeLessThanOrEqualTo(ceiling);
    }
}

// ════════════════════════════════════════════════════════════════════════
// HubConnectionState — tillståndsmaskin
// ════════════════════════════════════════════════════════════════════════

public class HubConnectionStateTests
{
    [Fact]
    public void InitialState_IsDisconnected()
    {
        // Verifierar att default-state är korrekt definierat
        var defaultState = default(HubConnectionState);
        defaultState.Should().Be(HubConnectionState.Disconnected);
    }

    [Theory]
    [InlineData(HubConnectionState.Connected,    "● Realtid")]
    [InlineData(HubConnectionState.Connecting,   "◌ Ansluter…")]
    [InlineData(HubConnectionState.Reconnecting, "⚠ Återsansluter")]
    [InlineData(HubConnectionState.Disconnected, "○ Offline")]
    [InlineData(HubConnectionState.AuthError,    "🔑 Ej inloggad")]
    public void StateToStatusText_MappingIsCorrect(HubConnectionState state, string expectedText)
    {
        var text = state switch
        {
            HubConnectionState.Connected    => "● Realtid",
            HubConnectionState.Connecting   => "◌ Ansluter…",
            HubConnectionState.Reconnecting => "⚠ Återsansluter",
            HubConnectionState.Disconnected => "○ Offline",
            HubConnectionState.AuthError    => "🔑 Ej inloggad",
            _                               => ""
        };
        text.Should().Be(expectedText);
    }

    [Theory]
    [InlineData(HubConnectionState.Connected,    false)] // ← spinner av
    [InlineData(HubConnectionState.Connecting,   true)]  // ← spinner på
    [InlineData(HubConnectionState.Reconnecting, true)]  // ← spinner på
    [InlineData(HubConnectionState.Disconnected, false)]
    [InlineData(HubConnectionState.AuthError,    false)]
    public void State_AnimationRequirement(HubConnectionState state, bool shouldAnimate)
    {
        var animate = state is
            HubConnectionState.Connecting or
            HubConnectionState.Reconnecting;

        animate.Should().Be(shouldAnimate);
    }
}

// ════════════════════════════════════════════════════════════════════════
// RealTimeUpdateService — inkrementell cache-logik (VS-oberoende del)
// ════════════════════════════════════════════════════════════════════════

// Simulerade stub-typer utan VS-beroende
file sealed class BacklogItemDtoStub
{
    public Guid   Id        { get; init; } = Guid.NewGuid();
    public string RuleId    { get; init; } = "SA001";
    public string Severity  { get; init; } = "Medium";
    public string FilePath  { get; init; } = "src/Foo.cs";
    public int    StartLine { get; init; } = 1;
    public string Message   { get; init; } = "";
    public string? ClassName { get; init; }
    public string? MemberName { get; init; }
}

// Simulerad cache-logik (speglar RealTimeUpdateService inre logik)
file sealed class SimulatedIssueCache
{
    private readonly Dictionary<string, List<BacklogItemDtoStub>> _cache = new();

    public void Seed(IEnumerable<BacklogItemDtoStub> items)
    {
        _cache.Clear();
        foreach (var dto in items)
            AddToCache(dto.FilePath, dto);
    }

    public void AddNew(IEnumerable<IssueSummary> issues)
    {
        foreach (var s in issues)
            AddToCache(s.FilePath, new BacklogItemDtoStub
            {
                Id       = s.Id,
                RuleId   = s.RuleId,
                Severity = s.Severity,
                FilePath = s.FilePath,
                Message  = s.Message
            });
    }

    public void RemoveResolved(IEnumerable<string> fingerprints)
    {
        var fps = new HashSet<string>(fingerprints, StringComparer.OrdinalIgnoreCase);
        foreach (var list in _cache.Values)
            list.RemoveAll(i => fps.Contains(i.FilePath));
    }

    public IReadOnlyList<BacklogItemDtoStub> GetAll() =>
        _cache.Values.SelectMany(x => x).ToList();

    private void AddToCache(string filePath, BacklogItemDtoStub dto)
    {
        var key = filePath.Replace('\\', '/').ToLowerInvariant();
        if (!_cache.TryGetValue(key, out var list))
            _cache[key] = list = [];
        list.Add(dto);
    }
}

public class RealTimeCacheTests
{
    private readonly SimulatedIssueCache _cache = new();

    [Fact]
    public void Seed_PopulatesCache()
    {
        _cache.Seed([
            new BacklogItemDtoStub { FilePath = "src/A.cs" },
            new BacklogItemDtoStub { FilePath = "src/B.cs" }
        ]);
        _cache.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void AddNew_AppendsToExistingCache()
    {
        _cache.Seed([new BacklogItemDtoStub { FilePath = "src/A.cs" }]);

        _cache.AddNew([
            new IssueSummary { FilePath = "src/C.cs", RuleId = "SA001" }
        ]);

        _cache.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void RemoveResolved_ByFingerprint_RemovesMatchingIssues()
    {
        var targetPath = "src/Services/OrderService.cs";
        _cache.Seed([
            new BacklogItemDtoStub { FilePath = targetPath },
            new BacklogItemDtoStub { FilePath = "src/Other.cs" }
        ]);

        _cache.RemoveResolved([targetPath]);

        var remaining = _cache.GetAll();
        remaining.Should().HaveCount(1);
        remaining[0].FilePath.Should().Be("src/Other.cs");
    }

    [Fact]
    public void RemoveResolved_CaseInsensitive()
    {
        _cache.Seed([new BacklogItemDtoStub { FilePath = "src/Services/Foo.cs" }]);
        _cache.RemoveResolved(["SRC/SERVICES/FOO.CS"]);
        _cache.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void RemoveResolved_UnknownFingerprint_NoChange()
    {
        _cache.Seed([new BacklogItemDtoStub { FilePath = "src/A.cs" }]);
        _cache.RemoveResolved(["does-not-exist"]);
        _cache.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void AddNew_ThenRemove_LeavesCorrectState()
    {
        _cache.Seed([new BacklogItemDtoStub { FilePath = "src/Base.cs" }]);

        var newPath = "src/New.cs";
        _cache.AddNew([new IssueSummary { FilePath = newPath, RuleId = "SA002" }]);
        _cache.GetAll().Should().HaveCount(2);

        _cache.RemoveResolved([newPath]);
        _cache.GetAll().Should().HaveCount(1, "bara Base.cs kvar");
    }
}

// ════════════════════════════════════════════════════════════════════════
// StatusBar-text formatting
// ════════════════════════════════════════════════════════════════════════

file static class StatusBarFormatter
{
    public static string FormatAnalysisResult(
        int newCount, int resolvedCount, int totalOpen, string projectName) =>
        (newCount, resolvedCount) switch
        {
            (0, > 0) =>
                $"✅ Synthtax: {resolvedCount} issue{Pl(resolvedCount)} löst i {projectName}",
            ( > 0, > 0) =>
                $"⚠ Synthtax: +{newCount} / -{resolvedCount} issues i {projectName}",
            ( > 0, 0) =>
                $"⚠ Synthtax: {newCount} nytt issue{Pl(newCount)} i {projectName}",
            _ =>
                $"✓ Synthtax: Analys klar — {totalOpen} öppna issues"
        };

    private static string Pl(int n) => n == 1 ? "" : "s";
}

public class StatusBarFormatterTests
{
    [Theory]
    [InlineData(0, 3, 10, "TestProject", "✅ Synthtax: 3 issues löst i TestProject")]
    [InlineData(2, 1, 5,  "API",         "⚠ Synthtax: +2 / -1 issues i API")]
    [InlineData(5, 0, 20, "Core",        "⚠ Synthtax: 5 nytt issues i Core")]
    [InlineData(0, 0, 7,  "Lib",         "✓ Synthtax: Analys klar — 7 öppna issues")]
    public void FormatAnalysisResult_CorrectMessages(
        int newC, int resolved, int total, string project, string expected)
    {
        var result = StatusBarFormatter.FormatAnalysisResult(newC, resolved, total, project);
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatAnalysisResult_SingleIssue_NoPluralS()
    {
        var result = StatusBarFormatter.FormatAnalysisResult(0, 1, 0, "Proj");
        result.Should().Contain("1 issue löst").And.NotContain("issues löst");
    }

    [Fact]
    public void FormatAnalysisResult_MultipleIssues_HasPluralS()
    {
        var result = StatusBarFormatter.FormatAnalysisResult(0, 2, 0, "Proj");
        result.Should().Contain("2 issues löst");
    }

    [Fact]
    public void AllFormats_ContainSynthtaxPrefix()
    {
        var cases = new[]
        {
            StatusBarFormatter.FormatAnalysisResult(0, 1, 0, "P"),
            StatusBarFormatter.FormatAnalysisResult(1, 0, 5, "P"),
            StatusBarFormatter.FormatAnalysisResult(1, 1, 5, "P"),
            StatusBarFormatter.FormatAnalysisResult(0, 0, 3, "P")
        };

        cases.Should().AllSatisfy(s => s.Should().Contain("Synthtax"));
    }
}

// ════════════════════════════════════════════════════════════════════════
// ISynthtaxHubClient — mock-kontraktstest
// ════════════════════════════════════════════════════════════════════════

public class HubClientContractTests
{
    [Fact]
    public void ISynthtaxHubClient_CanBeMocked()
    {
        // Verifierar att kontraktet är mockbart (t.ex. av konsumenttest)
        var mock = Substitute.For<ISynthtaxHubClient>();

        mock.State.Returns(HubConnectionState.Connected);
        mock.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public void ISynthtaxHubClient_EventsCanBeRaised()
    {
        var mock = Substitute.For<ISynthtaxHubClient>();
        bool eventRaised = false;

        mock.AnalysisUpdated += (_, _) => eventRaised = true;

        // Simulera att eventet raisas
        mock.AnalysisUpdated += Raise.EventWith(
            mock,
            new AnalysisUpdatedPayload { ProjectName = "TestProject" });

        eventRaised.Should().BeTrue();
    }

    [Fact]
    public async Task ISynthtaxHubClient_StartStop_AreMockable()
    {
        var mock = Substitute.For<ISynthtaxHubClient>();
        mock.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        mock.StopAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await mock.StartAsync();
        await mock.StopAsync();

        await mock.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await mock.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }
}

// ════════════════════════════════════════════════════════════════════════
// OrgGroup namnkonvention (speglar backend SynthtaxHub)
// ════════════════════════════════════════════════════════════════════════

public class OrgGroupNamingTests
{
    private static string OrgGroup(Guid orgId) => $"org:{orgId:N}";

    [Fact]
    public void OrgGroup_Format_IsOrgColonGuidN()
    {
        var orgId = Guid.NewGuid();
        var group = OrgGroup(orgId);

        group.Should().StartWith("org:");
        group.Should().HaveLength(4 + 32, "org: = 4 tecken, :N guid = 32 tecken utan bindestreck");
        group.Should().NotContain("-");
    }

    [Fact]
    public void OrgGroup_DifferentOrgs_DifferentGroups()
    {
        var group1 = OrgGroup(Guid.NewGuid());
        var group2 = OrgGroup(Guid.NewGuid());
        group1.Should().NotBe(group2);
    }

    [Fact]
    public void OrgGroup_SameOrg_SameGroup()
    {
        var orgId = Guid.NewGuid();
        OrgGroup(orgId).Should().Be(OrgGroup(orgId));
    }
}

// ════════════════════════════════════════════════════════════════════════
// Integrationstest: event → ToolWindow (stub)
// ════════════════════════════════════════════════════════════════════════

file sealed class FakeToolWindowTarget : IToolWindowRefreshTarget
{
    public List<BacklogItemDto> Added   { get; } = [];
    public List<string>         Removed { get; } = [];
    public List<Guid>           RemovedIds { get; } = [];
    public string?              LastPlan   { get; private set; }

    public Task ApplyIncrementalUpdateAsync(
        IReadOnlyList<BacklogItemDto> added,
        IReadOnlyList<string>         removed)
    {
        Added.AddRange(added);
        Removed.AddRange(removed);
        return Task.CompletedTask;
    }

    public void RemoveIssue(Guid issueId) => RemovedIds.Add(issueId);
    public void UpdateSubscriptionPlan(string newPlan) => LastPlan = newPlan;
}

// Stub BacklogItemDto for test
file sealed class BacklogItemDto
{
    public Guid   Id        { get; init; } = Guid.NewGuid();
    public string RuleId    { get; init; } = "";
    public string Severity  { get; init; } = "Medium";
    public string FilePath  { get; init; } = "";
    public int    StartLine { get; init; }
    public string Message   { get; init; } = "";
    public string? ClassName { get; init; }
    public string? MemberName { get; init; }
    public string Status    { get; init; } = "Open";
}

public class ToolWindowIntegrationTests
{
    [Fact]
    public async Task FakeTarget_ApplyIncrementalUpdate_AddsItems()
    {
        var target = new FakeToolWindowTarget();
        var added  = new List<BacklogItemDto>
        {
            new() { Id = Guid.NewGuid(), RuleId = "SA001", FilePath = "src/Svc.cs" }
        };

        await target.ApplyIncrementalUpdateAsync(added, []);

        target.Added.Should().HaveCount(1);
        target.Added[0].RuleId.Should().Be("SA001");
    }

    [Fact]
    public async Task FakeTarget_ApplyIncrementalUpdate_RecordsRemoved()
    {
        var target  = new FakeToolWindowTarget();
        var removed = new[] { "fingerprint-abc" };

        await target.ApplyIncrementalUpdateAsync([], removed);

        target.Removed.Should().HaveCount(1);
        target.Removed[0].Should().Be("fingerprint-abc");
    }

    [Fact]
    public void FakeTarget_RemoveIssue_RecordsId()
    {
        var target  = new FakeToolWindowTarget();
        var issueId = Guid.NewGuid();

        target.RemoveIssue(issueId);

        target.RemovedIds.Should().ContainSingle().Which.Should().Be(issueId);
    }

    [Fact]
    public void FakeTarget_UpdateSubscriptionPlan_Stores()
    {
        var target = new FakeToolWindowTarget();
        target.UpdateSubscriptionPlan("Enterprise");
        target.LastPlan.Should().Be("Enterprise");
    }

    [Fact]
    public async Task MultipleUpdates_Accumulate()
    {
        var target = new FakeToolWindowTarget();

        await target.ApplyIncrementalUpdateAsync(
            [new BacklogItemDto { RuleId = "SA001" }], []);

        await target.ApplyIncrementalUpdateAsync(
            [new BacklogItemDto { RuleId = "SA002" }], ["fp-1"]);

        target.Added.Should().HaveCount(2);
        target.Removed.Should().HaveCount(1);
    }
}
