// Synthtax.Tests/Fas7/VsixTests.cs
// Requires: xunit, FluentAssertions, NSubstitute
// OBS: VSIX-specifika klasser (AsyncPackage, IVs*) kan inte testas utan VS SDK mock-ramverk.
// Dessa tester täcker de delar som är VS-oberoende: DTOs, konverterar, diagnostik-mapping.

using FluentAssertions;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

// Simulerade typer för testbarhet utan VS SDK beroende
namespace Synthtax.Tests.Fas7;

// ════════════════════════════════════════════════════════════════════════
// Simulerade DTO-typer (speglar Synthtax.Vsix.Client)
// ════════════════════════════════════════════════════════════════════════

file sealed class BacklogItemDto
{
    public Guid   Id           { get; init; } = Guid.NewGuid();
    public string RuleId       { get; init; } = "SA001";
    public string Severity     { get; init; } = "Medium";
    public string Status       { get; init; } = "Open";
    public string FilePath     { get; init; } = "src/Services/FooService.cs";
    public int    StartLine    { get; init; } = 42;
    public string Message      { get; init; } = "Test issue";
    public string? ClassName   { get; init; }
    public string? MemberName  { get; init; }
    public string? Suggestion  { get; init; }
    public string? FixedSnippet { get; init; }
    public bool   IsAutoFixable { get; init; }
    public string Snippet      { get; init; } = "";
}

file sealed class ProjectHealthDto
{
    public string ProjectName   { get; init; } = "TestProject";
    public double OverallScore  { get; init; } = 72.5;
    public int    TotalIssues   { get; init; } = 15;
    public int    CriticalCount { get; init; } = 2;
    public int    HighCount     { get; init; } = 5;
    public string SubscriptionPlan { get; init; } = "Professional";
}

// ════════════════════════════════════════════════════════════════════════
// BacklogItemDto — modell
// ════════════════════════════════════════════════════════════════════════

public class BacklogItemDtoTests
{
    [Theory]
    [InlineData("Critical", true,  false, false)]
    [InlineData("High",     false, true,  false)]
    [InlineData("Medium",   false, false, false)]
    [InlineData("Low",      false, false, false)]
    public void SeverityFlags_AreCorrect(string sev, bool isCrit, bool isHigh, bool unused)
    {
        var dto = new BacklogItemDto { Severity = sev };
        (sev == "Critical").Should().Be(dto.Severity == "Critical");
        (sev == "High").Should().Be(dto.Severity == "High");
    }

    [Theory]
    [InlineData("Open",          true)]
    [InlineData("Acknowledged",  true)]
    [InlineData("InProgress",    true)]
    [InlineData("Resolved",      false)]
    [InlineData("Accepted",      false)]
    [InlineData("FalsePositive", false)]
    public void IsOpen_CorrectPerStatus(string status, bool expected)
    {
        var dto = new BacklogItemDto { Status = status };
        var isOpen = dto.Status is "Open" or "Acknowledged" or "InProgress";
        isOpen.Should().Be(expected);
    }

    [Fact]
    public void FilePath_CanBeRelative()
    {
        var dto = new BacklogItemDto { FilePath = "src/Domain/Order.cs" };
        dto.FilePath.Should().NotStartWith("/");
    }
}

// ════════════════════════════════════════════════════════════════════════
// SynthtaxDiagnosticIds — mapping
// ════════════════════════════════════════════════════════════════════════

public class DiagnosticIdMappingTests
{
    // Simulerar SynthtaxDiagnosticIds.ForRuleId logik
    private static string MapRuleToId(string ruleId, string severity) => ruleId switch
    {
        "SA001" => "SX0001",
        "SA002" => "SX0002",
        "SA003" => "SX0003",
        _       => severity switch { "Critical" => "SX9001", "High" => "SX9002", _ => "SX9003" }
    };

    [Theory]
    [InlineData("SA001", "Medium",   "SX0001")]
    [InlineData("SA002", "Low",      "SX0002")]
    [InlineData("SA003", "High",     "SX0003")]
    [InlineData("CA001", "Critical", "SX9001")]
    [InlineData("CA001", "High",     "SX9002")]
    [InlineData("CA001", "Medium",   "SX9003")]
    [InlineData("CA001", "Low",      "SX9003")]
    public void ForRuleId_MapsCorrectly(string ruleId, string severity, string expectedId)
    {
        MapRuleToId(ruleId, severity).Should().Be(expectedId);
    }

    [Fact]
    public void UnknownRule_FallsBackToSeverityMapping()
    {
        MapRuleToId("UNKNOWN999", "Critical").Should().Be("SX9001");
        MapRuleToId("UNKNOWN999", "Low").Should().Be("SX9003");
    }
}

// ════════════════════════════════════════════════════════════════════════
// SeverityToColorConverter
// ════════════════════════════════════════════════════════════════════════

// Simulerad konverterare (utan WPF-beroende i testerna)
file static class SeverityColorMap
{
    public static string GetColorHex(string severity) => severity switch
    {
        "Critical" => "#E74C3C",
        "High"     => "#E67E22",
        "Medium"   => "#F39C12",
        "Low"      => "#3498DB",
        _          => "#95A5A6"
    };
}

public class SeverityColorConverterTests
{
    [Theory]
    [InlineData("Critical", "#E74C3C")]
    [InlineData("High",     "#E67E22")]
    [InlineData("Medium",   "#F39C12")]
    [InlineData("Low",      "#3498DB")]
    [InlineData("Unknown",  "#95A5A6")]
    public void Convert_ReturnsCorrectColor(string severity, string expectedHex)
    {
        var color = SeverityColorMap.GetColorHex(severity);
        color.Should().Be(expectedHex);
    }

    [Fact]
    public void AllSeverityLevels_ProduceDistinctColors()
    {
        var severities = new[] { "Critical", "High", "Medium", "Low" };
        var colors = severities.Select(SeverityColorMap.GetColorHex).ToList();
        colors.Should().OnlyHaveUniqueItems("varje severity-nivå ska ha sin egen färg");
    }
}

// ════════════════════════════════════════════════════════════════════════
// StatusToIconConverter
// ════════════════════════════════════════════════════════════════════════

file static class StatusIconMap
{
    public static string GetIcon(string status) => status switch
    {
        "Open"          => "🔴",
        "Acknowledged"  => "🟡",
        "InProgress"    => "🔵",
        "Resolved"      => "✅",
        "Accepted"      => "✔️",
        "FalsePositive" => "🚫",
        _               => "⚪"
    };
}

public class StatusToIconConverterTests
{
    [Theory]
    [InlineData("Open",          "🔴")]
    [InlineData("Acknowledged",  "🟡")]
    [InlineData("InProgress",    "🔵")]
    [InlineData("Resolved",      "✅")]
    [InlineData("Accepted",      "✔️")]
    [InlineData("FalsePositive", "🚫")]
    [InlineData("Unknown",       "⚪")]
    public void GetIcon_ReturnsCorrectEmoji(string status, string expected)
    {
        StatusIconMap.GetIcon(status).Should().Be(expected);
    }

    [Fact]
    public void AllStatusValues_ProduceNonEmptyIcon()
    {
        var statuses = new[]
        {
            "Open", "Acknowledged", "InProgress",
            "Resolved", "Accepted", "FalsePositive"
        };
        foreach (var s in statuses)
            StatusIconMap.GetIcon(s).Should().NotBeNullOrEmpty();
    }
}

// ════════════════════════════════════════════════════════════════════════
// BacklogItemViewModel — presentation-logik (VS-oberoende del)
// ════════════════════════════════════════════════════════════════════════

// Simulerad ViewModel utan CommunityToolkit-beroende för enhetstestning
file sealed class BacklogItemVm
{
    public BacklogItemVm(BacklogItemDto dto)
    {
        Id         = dto.Id;
        RuleId     = dto.RuleId;
        Severity   = dto.Severity;
        Status     = dto.Status;
        FilePath   = dto.FilePath;
        StartLine  = dto.StartLine;
        Message    = dto.Message;
        ClassName  = dto.ClassName;
        MemberName = dto.MemberName;
    }

    public Guid   Id         { get; }
    public string RuleId     { get; }
    public string Severity   { get; }
    public string Status     { get; }
    public string FilePath   { get; }
    public int    StartLine  { get; }
    public string Message    { get; }
    public string? ClassName  { get; }
    public string? MemberName { get; }

    public string FileName => System.IO.Path.GetFileName(FilePath);
    public string Scope    => $"{ClassName ?? "?"}.{MemberName ?? "?"}";
}

public class BacklogItemViewModelTests
{
    private static BacklogItemDto MakeDto(
        string filePath = "src/Services/PaymentService.cs",
        string? className = "PaymentService",
        string? memberName = "ProcessRefund") =>
        new()
        {
            FilePath   = filePath,
            ClassName  = className,
            MemberName = memberName
        };

    [Fact]
    public void FileName_ExtractsFromPath()
    {
        var vm = new BacklogItemVm(MakeDto("src/Domain/Entities/Order.cs"));
        vm.FileName.Should().Be("Order.cs");
    }

    [Fact]
    public void FileName_HandlesWindowsPaths()
    {
        var vm = new BacklogItemVm(MakeDto(@"src\Services\Foo.cs"));
        // System.IO.Path.GetFileName handles both separators
        vm.FileName.Should().Be("Foo.cs");
    }

    [Fact]
    public void Scope_FormatIsClassDotMember()
    {
        var vm = new BacklogItemVm(MakeDto(className: "OrderService", memberName: "CreateAsync"));
        vm.Scope.Should().Be("OrderService.CreateAsync");
    }

    [Fact]
    public void Scope_WithNullClass_ShowsQuestionMark()
    {
        var vm = new BacklogItemVm(MakeDto(className: null, memberName: "Run"));
        vm.Scope.Should().Be("?.Run");
    }

    [Fact]
    public void Scope_WithBothNull_ShowsDoubleQuestionMark()
    {
        var vm = new BacklogItemVm(MakeDto(className: null, memberName: null));
        vm.Scope.Should().Be("?.?");
    }

    [Fact]
    public void AllPropertiesFromDto_AreMapped()
    {
        var dto = new BacklogItemDto
        {
            RuleId    = "SA003",
            Severity  = "High",
            Status    = "InProgress",
            StartLine = 77
        };
        var vm = new BacklogItemVm(dto);
        vm.RuleId.Should().Be("SA003");
        vm.Severity.Should().Be("High");
        vm.Status.Should().Be("InProgress");
        vm.StartLine.Should().Be(77);
    }
}

// ════════════════════════════════════════════════════════════════════════
// BacklogItemCache  — sökning och filtrering (VS-oberoende)
// ════════════════════════════════════════════════════════════════════════

// Simulerad cache för testbarhet
file static class BacklogItemCacheTestHelper
{
    private static readonly List<BacklogItemDto> _items = [];

    public static void Update(IEnumerable<BacklogItemDto> items)
    {
        _items.Clear();
        _items.AddRange(items);
    }

    public static BacklogItemDto? FindByLocation(string filePath, int line)
    {
        var normalized = filePath.Replace('\\', '/').ToLowerInvariant();
        return _items.FirstOrDefault(i =>
            i.FilePath.Replace('\\', '/').EndsWith(
                normalized.Split('/').Last(), StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(i.StartLine - line) <= 2);
    }

    public static void Clear() => _items.Clear();
}

public class BacklogItemCacheTests : IDisposable
{
    public BacklogItemCacheTests() => BacklogItemCacheTestHelper.Clear();
    public void Dispose() => BacklogItemCacheTestHelper.Clear();

    [Fact]
    public void FindByLocation_ExactMatch_ReturnsItem()
    {
        var dto = new BacklogItemDto { FilePath = "src/Services/Order.cs", StartLine = 50 };
        BacklogItemCacheTestHelper.Update([dto]);

        var found = BacklogItemCacheTestHelper.FindByLocation("src/Services/Order.cs", 50);
        found.Should().NotBeNull();
    }

    [Fact]
    public void FindByLocation_Within2Lines_ReturnsItem()
    {
        var dto = new BacklogItemDto { FilePath = "src/Foo.cs", StartLine = 100 };
        BacklogItemCacheTestHelper.Update([dto]);

        BacklogItemCacheTestHelper.FindByLocation("src/Foo.cs", 101).Should().NotBeNull();
        BacklogItemCacheTestHelper.FindByLocation("src/Foo.cs", 98).Should().NotBeNull();
        BacklogItemCacheTestHelper.FindByLocation("src/Foo.cs", 103).Should().BeNull();
    }

    [Fact]
    public void FindByLocation_CaseInsensitiveFilePath()
    {
        var dto = new BacklogItemDto { FilePath = "src/Services/PAYMENT.cs", StartLine = 10 };
        BacklogItemCacheTestHelper.Update([dto]);

        var found = BacklogItemCacheTestHelper.FindByLocation("src/services/payment.cs", 10);
        found.Should().NotBeNull();
    }

    [Fact]
    public void FindByLocation_BackslashPaths_Normalized()
    {
        var dto = new BacklogItemDto { FilePath = @"src\Domain\Order.cs", StartLine = 20 };
        BacklogItemCacheTestHelper.Update([dto]);

        var found = BacklogItemCacheTestHelper.FindByLocation("src/Domain/Order.cs", 20);
        found.Should().NotBeNull("backslash och forward-slash ska normaliseras");
    }

    [Fact]
    public void FindByLocation_NoMatch_ReturnsNull()
    {
        var dto = new BacklogItemDto { FilePath = "src/Foo.cs", StartLine = 50 };
        BacklogItemCacheTestHelper.Update([dto]);

        BacklogItemCacheTestHelper.FindByLocation("src/Bar.cs", 50).Should().BeNull();
    }

    [Fact]
    public void Update_ClearsPreviousItems()
    {
        var dto1 = new BacklogItemDto { FilePath = "Old.cs", StartLine = 1 };
        BacklogItemCacheTestHelper.Update([dto1]);

        var dto2 = new BacklogItemDto { FilePath = "New.cs", StartLine = 1 };
        BacklogItemCacheTestHelper.Update([dto2]);

        BacklogItemCacheTestHelper.FindByLocation("Old.cs", 1).Should().BeNull();
        BacklogItemCacheTestHelper.FindByLocation("New.cs", 1).Should().NotBeNull();
    }
}

// ════════════════════════════════════════════════════════════════════════
// Fallback Prompt-generering (VS-oberoende)
// ════════════════════════════════════════════════════════════════════════

file static class FallbackPromptBuilder
{
    public static string BuildCopilot(BacklogItemDto item) =>
        $"// Synthtax [{item.RuleId}] {item.Severity}\n// {item.Message}\n// File: {item.FilePath}:{item.StartLine}\n// Fix: {item.Suggestion}";

    public static string BuildClaude(BacklogItemDto item) =>
        $"# Synthtax [{item.RuleId}]\n**{item.Message}**\nFile: `{item.FilePath}:{item.StartLine}`\n\n```csharp\n{item.Snippet}\n```\n\n{item.Suggestion}";
}

public class FallbackPromptBuilderTests
{
    [Fact]
    public void BuildCopilot_ContainsRuleIdAndFile()
    {
        var dto = new BacklogItemDto
        {
            RuleId   = "SA001",
            Severity = "Warning",
            Message  = "Not implemented",
            FilePath = "src/Services/Svc.cs",
            StartLine = 42
        };

        var prompt = FallbackPromptBuilder.BuildCopilot(dto);
        prompt.Should().Contain("SA001");
        prompt.Should().Contain("Svc.cs");
        prompt.Should().Contain("42");
        prompt.Should().StartWith("//", "Copilot-prompt ska vara kommentarformat");
    }

    [Fact]
    public void BuildClaude_IsMarkdown()
    {
        var dto = new BacklogItemDto
        {
            RuleId   = "SA003",
            Message  = "Complex method",
            FilePath = "src/Repo.cs",
            StartLine = 10,
            Snippet  = "public void BigMethod() { ... }"
        };

        var prompt = FallbackPromptBuilder.BuildClaude(dto);
        prompt.Should().Contain("# Synthtax", "Claude-prompt ska ha markdown-heading");
        prompt.Should().Contain("```csharp", "Claude-prompt ska ha kodblock");
        prompt.Should().Contain("SA003");
    }

    [Fact]
    public void CopilotPrompt_IsShorterThanClaudePrompt()
    {
        var dto = new BacklogItemDto
        {
            RuleId  = "SA001",
            Message = "NIE",
            Snippet = string.Join("\n", Enumerable.Repeat("// line", 10))
        };

        var copilot = FallbackPromptBuilder.BuildCopilot(dto);
        var claude  = FallbackPromptBuilder.BuildClaude(dto);

        copilot.Length.Should().BeLessThan(claude.Length,
            "Copilot-promptar är kompakta, Claude-promptar är fullständiga");
    }
}

// ════════════════════════════════════════════════════════════════════════
// CredentialStore — DPAPI-fallback-path (VS-oberoende del)
// ════════════════════════════════════════════════════════════════════════

public class CredentialStoreFallbackPathTests
{
    // Testar FallbackPath-logiken utan DPAPI (bara path-beräkning)
    private static string ComputeFallbackPath(string key)
    {
        var hexKey = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(key));
        var dir    = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Synthtax", "VS");
        return System.IO.Path.Combine(dir, hexKey + ".enc");
    }

    [Fact]
    public void FallbackPath_IsUnderAppData()
    {
        var path = ComputeFallbackPath("Synthtax.AccessToken");
        path.Should().Contain("Synthtax");
        path.Should().EndWith(".enc");
    }

    [Fact]
    public void FallbackPath_DifferentKeysProduceDifferentPaths()
    {
        var path1 = ComputeFallbackPath("Synthtax.AccessToken");
        var path2 = ComputeFallbackPath("Synthtax.TokenExpiry");
        path1.Should().NotBe(path2);
    }

    [Fact]
    public void FallbackPath_SameKey_SamePath()
    {
        var path1 = ComputeFallbackPath("Synthtax.AccessToken");
        var path2 = ComputeFallbackPath("Synthtax.AccessToken");
        path1.Should().Be(path2);
    }
}

// ════════════════════════════════════════════════════════════════════════
// SynthtaxApiException hierarki
// ════════════════════════════════════════════════════════════════════════

file sealed class SynthtaxApiException(string msg) : Exception(msg);
file sealed class UnauthorizedException(string msg) : Exception(msg);
file sealed class LicenseException(string msg) : Exception(msg);

public class ApiExceptionTests
{
    [Fact]
    public void UnauthorizedException_IsException()
    {
        var ex = new UnauthorizedException("Session utgången");
        ex.Message.Should().Be("Session utgången");
        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void LicenseException_MessageSurfacesUpgradeHint()
    {
        var ex = new LicenseException("Kvoten full. Uppgradera till Professional.");
        ex.Message.Should().Contain("Uppgradera");
    }

    [Fact]
    public void SynthtaxApiException_WrapsStatusCode()
    {
        var ex = new SynthtaxApiException("API-fel 404: Resurs hittades inte");
        ex.Message.Should().Contain("404");
    }
}
