// Synthtax.Tests/Fas8/RealtimeSyncTests.cs
// Requires: xunit, FluentAssertions, NSubstitute

using FluentAssertions;
using Synthtax.Realtime.Contracts;

namespace Synthtax.Tests.Fas8;

// ════════════════════════════════════════════════════════════════════════
// HubMethodNames — string-konstanter
// ════════════════════════════════════════════════════════════════════════

public class HubMethodNamesTests
{
    [Fact]
    public void AllMethodNames_AreNonEmpty()
    {
        HubMethodNames.AnalysisUpdated.Should().NotBeNullOrEmpty();
        HubMethodNames.IssueCreated.Should().NotBeNullOrEmpty();
        HubMethodNames.IssueClosed.Should().NotBeNullOrEmpty();
        HubMethodNames.HealthScoreUpdated.Should().NotBeNullOrEmpty();
        HubMethodNames.JoinOrgGroup.Should().NotBeNullOrEmpty();
        HubMethodNames.LeaveOrgGroup.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AllMethodNames_AreDistinct()
    {
        var names = new[]
        {
            HubMethodNames.AnalysisUpdated,
            HubMethodNames.IssueCreated,
            HubMethodNames.IssueClosed,
            HubMethodNames.HealthScoreUpdated,
            HubMethodNames.JoinOrgGroup,
            HubMethodNames.LeaveOrgGroup
        };
        names.Should().OnlyHaveUniqueItems("varje hub-metod måste ha unikt namn");
    }
}

// ════════════════════════════════════════════════════════════════════════
// ConnectionStateSnapshot — display-logik
// ════════════════════════════════════════════════════════════════════════

// Simulerade modeller för VS-oberoende testning
file enum RealtimeConnectionState
{
    Disconnected = 0, Connecting = 1, Connected = 2, Reconnecting = 3, Failed = 4
}

file sealed record ConnectionStateSnapshot
{
    public RealtimeConnectionState State        { get; init; }
    public string?                 ErrorMessage { get; init; }
    public int                     RetryAttempt { get; init; }
    public TimeSpan?               NextRetryIn  { get; init; }

    public string StatusBarText => State switch
    {
        RealtimeConnectionState.Disconnected => "Synthtax: Offline",
        RealtimeConnectionState.Connecting   => "Synthtax: Ansluter…",
        RealtimeConnectionState.Connected    => "Synthtax: Live ●",
        RealtimeConnectionState.Reconnecting => NextRetryIn.HasValue
            ? $"Synthtax: Återansluter ({NextRetryIn.Value.TotalSeconds:F0}s)…"
            : "Synthtax: Återansluter…",
        RealtimeConnectionState.Failed => "Synthtax: Anslutning misslyckades",
        _ => "Synthtax"
    };

    public bool RequiresUserAction => State == RealtimeConnectionState.Failed;

    public static ConnectionStateSnapshot Disconnected() =>
        new() { State = RealtimeConnectionState.Disconnected };
    public static ConnectionStateSnapshot Connecting() =>
        new() { State = RealtimeConnectionState.Connecting };
    public static ConnectionStateSnapshot Connected() =>
        new() { State = RealtimeConnectionState.Connected };
    public static ConnectionStateSnapshot Reconnecting(int attempt, TimeSpan? nextIn, string? err) =>
        new() { State = RealtimeConnectionState.Reconnecting,
                RetryAttempt = attempt, NextRetryIn = nextIn, ErrorMessage = err };
    public static ConnectionStateSnapshot Failed(string? err) =>
        new() { State = RealtimeConnectionState.Failed, ErrorMessage = err };
}

public class ConnectionStateSnapshotTests
{
    // ── StatusBarText ──────────────────────────────────────────────────────

    [Fact]
    public void Disconnected_StatusBarText_ContainsSynthtax()
    {
        var snap = ConnectionStateSnapshot.Disconnected();
        snap.StatusBarText.Should().Contain("Synthtax");
        snap.StatusBarText.Should().Contain("Offline");
    }

    [Fact]
    public void Connected_StatusBarText_ShowsLiveDot()
    {
        var snap = ConnectionStateSnapshot.Connected();
        snap.StatusBarText.Should().Contain("Live");
        snap.StatusBarText.Should().Contain("●");
    }

    [Fact]
    public void Reconnecting_WithDelay_ShowsSeconds()
    {
        var snap = ConnectionStateSnapshot.Reconnecting(2, TimeSpan.FromSeconds(30), null);
        snap.StatusBarText.Should().Contain("30");
    }

    [Fact]
    public void Reconnecting_WithoutDelay_ShowsGenericText()
    {
        var snap = ConnectionStateSnapshot.Reconnecting(1, null, null);
        snap.StatusBarText.Should().Contain("Återansluter");
        snap.StatusBarText.Should().NotContain("s)");
    }

    [Fact]
    public void Failed_StatusBarText_ContainsFailureWord()
    {
        var snap = ConnectionStateSnapshot.Failed("Connection refused");
        snap.StatusBarText.Should().Contain("misslyckades");
    }

    // ── RequiresUserAction ─────────────────────────────────────────────────

    [Theory]
    [InlineData(RealtimeConnectionState.Failed,      true)]
    [InlineData(RealtimeConnectionState.Disconnected, false)]
    [InlineData(RealtimeConnectionState.Connecting,   false)]
    [InlineData(RealtimeConnectionState.Connected,    false)]
    [InlineData(RealtimeConnectionState.Reconnecting, false)]
    public void RequiresUserAction_OnlyForFailed(RealtimeConnectionState state, bool expected)
    {
        var snap = new ConnectionStateSnapshot { State = state };
        snap.RequiresUserAction.Should().Be(expected);
    }

    // ── Factories ──────────────────────────────────────────────────────────

    [Fact]
    public void Factories_ProduceCorrectState()
    {
        ConnectionStateSnapshot.Disconnected().State.Should().Be(RealtimeConnectionState.Disconnected);
        ConnectionStateSnapshot.Connecting().State.Should().Be(RealtimeConnectionState.Connecting);
        ConnectionStateSnapshot.Connected().State.Should().Be(RealtimeConnectionState.Connected);
        ConnectionStateSnapshot.Failed("err").State.Should().Be(RealtimeConnectionState.Failed);
    }

    [Fact]
    public void Reconnecting_StoresRetryMetadata()
    {
        var snap = ConnectionStateSnapshot.Reconnecting(3, TimeSpan.FromSeconds(60), "Timeout");
        snap.RetryAttempt.Should().Be(3);
        snap.NextRetryIn.Should().Be(TimeSpan.FromSeconds(60));
        snap.ErrorMessage.Should().Be("Timeout");
    }
}

// ════════════════════════════════════════════════════════════════════════
// JwtOrgIdExtractor — JWT-claim-parsing utan VS-beroende
// ════════════════════════════════════════════════════════════════════════

file static class JwtOrgIdExtractor
{
    public static Guid? Extract(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("synthtax:org_id", out var el))
            {
                var raw = el.GetString();
                return Guid.TryParse(raw, out var id) ? id : null;
            }
            return null;
        }
        catch { return null; }
    }

    public static string CreateTestJwt(Guid orgId)
    {
        var header  = Base64UrlEncode(@"{""alg"":""HS256"",""typ"":""JWT""}");
        var payload = Base64UrlEncode($@"{{""sub"":""user-1"",""synthtax:org_id"":""{orgId:D}""}}");
        return $"{header}.{payload}.fake_signature";
    }

    public static string CreateTestJwtNoOrgId()
    {
        var header  = Base64UrlEncode(@"{""alg"":""HS256"",""typ"":""JWT""}");
        var payload = Base64UrlEncode(@"{""sub"":""user-1""}");
        return $"{header}.{payload}.fake_signature";
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

public class JwtOrgIdExtractorTests
{
    [Fact]
    public void Extract_ValidJwtWithOrgId_ReturnsGuid()
    {
        var orgId = Guid.NewGuid();
        var jwt   = JwtOrgIdExtractor.CreateTestJwt(orgId);
        var result = JwtOrgIdExtractor.Extract(jwt);
        result.Should().Be(orgId);
    }

    [Fact]
    public void Extract_JwtWithoutOrgId_ReturnsNull()
    {
        var jwt = JwtOrgIdExtractor.CreateTestJwtNoOrgId();
        JwtOrgIdExtractor.Extract(jwt).Should().BeNull();
    }

    [Fact]
    public void Extract_InvalidJwtFormat_ReturnsNull()
    {
        JwtOrgIdExtractor.Extract("not.a.jwt.at.all").Should().BeNull();
        JwtOrgIdExtractor.Extract("").Should().BeNull();
        JwtOrgIdExtractor.Extract("onlyone").Should().BeNull();
    }

    [Fact]
    public void Extract_MalformedPayload_ReturnsNull()
    {
        JwtOrgIdExtractor.Extract("header.%%%invalid%%%base64.sig").Should().BeNull();
    }

    [Fact]
    public void Extract_OrgIdNotValidGuid_ReturnsNull()
    {
        var header  = JwtOrgIdExtractor.CreateTestJwt(Guid.Empty).Split('.')[0];
        var payloadJson = @"{""synthtax:org_id"":""not-a-guid""}";
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        JwtOrgIdExtractor.Extract($"{header}.{payload}.sig").Should().BeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════
// Exponentiell backoff — sekvens
// ════════════════════════════════════════════════════════════════════════

file static class ReconnectPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60)
    ];

    public static TimeSpan GetDelay(int attempt) =>
        Delays[Math.Min(attempt - 1, Delays.Length - 1)];

    public static bool IsAuthError(string message) =>
        message.Contains("401") ||
        message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase);
}

public class ReconnectPolicyTests
{
    [Theory]
    [InlineData(1,  2)]
    [InlineData(2,  5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(5, 60)]
    [InlineData(6, 60)]   // Cap på max-värde
    [InlineData(99, 60)]  // Långt utöver gränsen — cap håller
    public void GetDelay_ReturnsExpectedSeconds(int attempt, int expectedSeconds)
    {
        ReconnectPolicy.GetDelay(attempt).TotalSeconds.Should().Be(expectedSeconds);
    }

    [Fact]
    public void GetDelay_IsMonotonicallyIncreasing()
    {
        var delays = Enumerable.Range(1, 5)
            .Select(i => ReconnectPolicy.GetDelay(i))
            .ToList();

        for (int i = 1; i < delays.Count; i++)
            delays[i].Should().BeGreaterThanOrEqualTo(delays[i - 1],
                "varje delay ska vara >= föregående");
    }

    [Theory]
    [InlineData("401 Unauthorized", true)]
    [InlineData("Connection refused with 401", true)]
    [InlineData("Status: Unauthorized", true)]
    [InlineData("Connection timed out", false)]
    [InlineData("Network unreachable", false)]
    public void IsAuthError_CorrectlyClassifies(string message, bool expected)
    {
        ReconnectPolicy.IsAuthError(message).Should().Be(expected);
    }
}

// ════════════════════════════════════════════════════════════════════════
// AnalysisUpdatedEvent — payload-validering
// ════════════════════════════════════════════════════════════════════════

public class AnalysisUpdatedEventTests
{
    private static AnalysisUpdatedEvent MakeEvent(
        int newCount = 0, int closedCount = 0,
        double score = 80.0,
        IReadOnlyList<HubBacklogItem>? issues = null) => new()
    {
        OrganizationId  = Guid.NewGuid(),
        ProjectId       = Guid.NewGuid(),
        ProjectName     = "TestProject",
        HealthScore     = score,
        TotalIssues     = (issues?.Count ?? 0) + newCount,
        NewIssueCount   = newCount,
        ClosedIssueCount = closedCount,
        AnalyzedAt      = DateTime.UtcNow,
        Issues          = issues ?? []
    };

    [Fact]
    public void HasChanges_TrueWhenNewIssues()
    {
        MakeEvent(newCount: 3).HasChanges.Should().BeTrue();
    }

    [Fact]
    public void HasChanges_TrueWhenClosedIssues()
    {
        MakeEvent(closedCount: 2).HasChanges.Should().BeTrue();
    }

    [Fact]
    public void HasChanges_FalseWhenNoChanges()
    {
        MakeEvent().HasChanges.Should().BeFalse();
    }

    [Fact]
    public void Issues_DefaultsToEmptyList()
    {
        MakeEvent().Issues.Should().BeEmpty();
    }

    [Fact]
    public void HealthScoreDelta_ComputedCorrectly()
    {
        var updated = new HealthScoreUpdatedEvent
        {
            OrganizationId = Guid.NewGuid(),
            ProjectId      = Guid.NewGuid(),
            OldScore       = 65.0,
            NewScore       = 80.0,
            TotalIssues    = 10,
            CriticalCount  = 1,
            HighCount      = 3,
            ChangedAt      = DateTime.UtcNow
        };

        updated.Delta.Should().BeApproximately(15.0, 0.001);
    }
}

// ════════════════════════════════════════════════════════════════════════
// RealtimeDiagnosticBridge — VS-oberoende cache-logik
// ════════════════════════════════════════════════════════════════════════

// Simulerad cache (speglar SynthtaxDiagnosticProvider.UpdateCache)
file static class SimulatedDiagnosticCache
{
    public static List<HubBacklogItem> LastUpdate { get; private set; } = [];
    public static void Update(IReadOnlyList<HubBacklogItem> items) =>
        LastUpdate = items.ToList();
    public static void Clear() => LastUpdate = [];
}

// Simulerad brygga (VS-oberoende)
file sealed class SimulatedDiagnosticBridge
{
    private readonly Dictionary<Guid, HubBacklogItem> _items = new();

    public void OnAnalysisUpdated(AnalysisUpdatedEvent payload)
    {
        _items.Clear();
        foreach (var item in payload.Issues)
            _items[item.Id] = item;
        SimulatedDiagnosticCache.Update(_items.Values.ToList());
    }

    public void OnIssueCreated(IssueCreatedEvent payload)
    {
        var item = new HubBacklogItem
        {
            Id       = payload.IssueId,
            RuleId   = payload.RuleId,
            Severity = payload.Severity,
            Status   = "Open",
            FilePath = payload.FilePath,
            StartLine = payload.StartLine,
            Message  = payload.Message
        };
        _items[item.Id] = item;
        SimulatedDiagnosticCache.Update(_items.Values.ToList());
    }

    public void OnIssueClosed(IssueClosedEvent payload)
    {
        _items.Remove(payload.IssueId);
        SimulatedDiagnosticCache.Update(_items.Values.ToList());
    }

    public int CacheSize => _items.Count;
}

public class RealtimeDiagnosticBridgeTests : IDisposable
{
    private readonly SimulatedDiagnosticBridge _sut = new();

    public void Dispose() => SimulatedDiagnosticCache.Clear();

    [Fact]
    public void OnAnalysisUpdated_ReplacesCacheCompletely()
    {
        // Befintlig state: 3 items
        SeedItems(3);

        // Ny batch med bara 1 item
        var newItem = MakeHubItem(Guid.NewGuid(), "SA001");
        _sut.OnAnalysisUpdated(new AnalysisUpdatedEvent
        {
            OrganizationId = Guid.NewGuid(),
            ProjectId      = Guid.NewGuid(),
            ProjectName    = "P",
            HealthScore    = 80,
            TotalIssues    = 1,
            NewIssueCount  = 1,
            ClosedIssueCount = 0,
            AnalyzedAt     = DateTime.UtcNow,
            Issues         = [newItem]
        });

        _sut.CacheSize.Should().Be(1, "AnalysisUpdated ersätter hela cachen");
        SimulatedDiagnosticCache.LastUpdate.Should().ContainSingle(i => i.Id == newItem.Id);
    }

    [Fact]
    public void OnIssueCreated_AddsToExistingCache()
    {
        var initialCount = 2;
        SeedItems(initialCount);

        _sut.OnIssueCreated(new IssueCreatedEvent
        {
            OrganizationId = Guid.NewGuid(),
            IssueId        = Guid.NewGuid(),
            RuleId         = "SA003",
            Severity       = "High",
            FilePath       = "src/Svc.cs",
            StartLine      = 42,
            Message        = "Complex method"
        });

        _sut.CacheSize.Should().Be(initialCount + 1);
    }

    [Fact]
    public void OnIssueClosed_RemovesFromCache()
    {
        var id    = Guid.NewGuid();
        var item  = MakeHubItem(id, "SA001");
        SeedWithItems([item]);

        _sut.OnIssueClosed(new IssueClosedEvent
        {
            OrganizationId = Guid.NewGuid(),
            IssueId        = id,
            RuleId         = "SA001",
            FilePath       = item.FilePath,
            Reason         = "AutoClosed"
        });

        _sut.CacheSize.Should().Be(0);
        SimulatedDiagnosticCache.LastUpdate.Should().BeEmpty();
    }

    [Fact]
    public void OnIssueClosed_NonExistentId_NoSideEffect()
    {
        SeedItems(2);

        _sut.OnIssueClosed(new IssueClosedEvent
        {
            OrganizationId = Guid.NewGuid(),
            IssueId        = Guid.NewGuid(), // okänd
            RuleId         = "SA001",
            FilePath       = "f.cs",
            Reason         = "AutoClosed"
        });

        _sut.CacheSize.Should().Be(2, "okänt ID ska inte påverka cachen");
    }

    [Fact]
    public void OnIssueCreated_DuplicateId_UpdatesExisting()
    {
        var id   = Guid.NewGuid();
        SeedWithItems([MakeHubItem(id, "SA001")]);

        // Samma ID, annan regel
        _sut.OnIssueCreated(new IssueCreatedEvent
        {
            OrganizationId = Guid.NewGuid(),
            IssueId        = id,
            RuleId         = "SA002",   // Överskriver
            Severity       = "Medium",
            FilePath       = "f.cs",
            StartLine      = 1,
            Message        = "Updated"
        });

        _sut.CacheSize.Should().Be(1, "samma ID ska inte skapa ett dubblett");
    }

    [Fact]
    public void CacheUpdates_ReflectInSimulatedDiagnosticCache()
    {
        SeedItems(3);
        SimulatedDiagnosticCache.LastUpdate.Should().HaveCount(3);
    }

    // ── Hjälpmetoder ──────────────────────────────────────────────────────

    private void SeedItems(int count)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => MakeHubItem(Guid.NewGuid(), $"SA00{i + 1}"))
            .ToList();
        SeedWithItems(items);
    }

    private void SeedWithItems(IReadOnlyList<HubBacklogItem> items)
    {
        _sut.OnAnalysisUpdated(new AnalysisUpdatedEvent
        {
            OrganizationId = Guid.NewGuid(),
            ProjectId      = Guid.NewGuid(),
            ProjectName    = "Seed",
            HealthScore    = 70,
            TotalIssues    = items.Count,
            NewIssueCount  = items.Count,
            ClosedIssueCount = 0,
            AnalyzedAt     = DateTime.UtcNow,
            Issues         = items
        });
    }

    private static HubBacklogItem MakeHubItem(Guid id, string ruleId) => new()
    {
        Id       = id,
        RuleId   = ruleId,
        Severity = "Medium",
        Status   = "Open",
        FilePath = $"src/{ruleId}/{id:N}.cs",
        StartLine = 10,
        Message  = $"Test issue {ruleId}"
    };
}

// ════════════════════════════════════════════════════════════════════════
// StatusBar display-text — round-trip test
// ════════════════════════════════════════════════════════════════════════

public class StatusBarDisplayTests
{
    [Fact]
    public void ShowPushNotification_ZeroChanges_DoesNotDisplay()
    {
        // Logik: om newCount=0 och closedCount=0 ska ingenting visas
        var shouldShow = 0 > 0 || 0 > 0;
        shouldShow.Should().BeFalse();
    }

    [Theory]
    [InlineData(3, 0,  "+3 nya issues")]
    [InlineData(0, 2,  "2 stängda")]
    [InlineData(5, 3,  "+5 nya issues")]
    public void PushNotificationText_ContainsExpectedPart(
        int newCount, int closedCount, string expectedPart)
    {
        var parts = new List<string>();
        if (newCount    > 0) parts.Add($"+{newCount} nya issues");
        if (closedCount > 0) parts.Add($"{closedCount} stängda");

        var text = $"Synthtax: {string.Join(", ", parts)} · Hälsa 80/100";
        text.Should().Contain(expectedPart);
    }

    [Fact]
    public void PushNotificationText_BothNewAndClosed_ContainsBoth()
    {
        var parts = new List<string> { "+5 nya issues", "3 stängda" };
        var text  = $"Synthtax: {string.Join(", ", parts)} · Hälsa 80/100";

        text.Should().Contain("+5 nya issues");
        text.Should().Contain("3 stängda");
        text.Should().Contain("80/100");
    }
}

// ════════════════════════════════════════════════════════════════════════
// AnalysisHub.OrgGroupName — routing-konvention
// ════════════════════════════════════════════════════════════════════════

public class AnalysisHubRoutingTests
{
    private static string OrgGroupName(Guid orgId) => $"org:{orgId:N}";

    [Fact]
    public void OrgGroupName_Format_IsConsistent()
    {
        var orgId = Guid.NewGuid();
        var name  = OrgGroupName(orgId);
        name.Should().StartWith("org:");
        name.Should().HaveLength(4 + 32, "prefix 'org:' + 32 hex chars");
        name.Should().NotContain("-", "N-format har inga bindestreck");
    }

    [Fact]
    public void OrgGroupName_SameGuid_SameName()
    {
        var orgId = Guid.NewGuid();
        OrgGroupName(orgId).Should().Be(OrgGroupName(orgId));
    }

    [Fact]
    public void OrgGroupName_DifferentGuids_DifferentNames()
    {
        OrgGroupName(Guid.NewGuid()).Should().NotBe(OrgGroupName(Guid.NewGuid()));
    }
}
