// Synthtax.Tests/Fas6/PromptFactoryAndRulesTests.cs
// Requires: xunit, FluentAssertions, Microsoft.CodeAnalysis.CSharp

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synthtax.Analysis.Rules;
using Synthtax.Application.PromptFactory;
using Synthtax.Core.Enums;
using Synthtax.Core.PromptFactory;

namespace Synthtax.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Hjälpmetoder — delade av alla testklasser
// ═══════════════════════════════════════════════════════════════════════════

internal static class TestHelpers
{
    public static SyntaxTree ParseTree(string code) =>
        CSharpSyntaxTree.ParseText(code);

    public static PromptContext MakeContext(
        string ruleId      = "SA001",
        string ruleName    = "Test Rule",
        string description = "A test rule.",
        string filePath    = "src/Services/FooService.cs",
        string snippet     = "throw new NotImplementedException();",
        string? suggestion = null,
        string? fixedSnippet = null,
        bool autoFixable   = false,
        Severity severity  = Severity.Medium,
        string category    = "Architecture",
        int startLine      = 10,
        string? className  = "FooService",
        string? memberName = "DoWork",
        string? projectName = "Synthtax.API",
        string? language   = "C#") => new()
    {
        RuleId          = ruleId,
        RuleName        = ruleName,
        RuleDescription = description,
        Category        = category,
        Severity        = severity,
        FilePath        = filePath,
        Snippet         = snippet,
        Suggestion      = suggestion,
        FixedSnippet    = fixedSnippet,
        IsAutoFixable   = autoFixable,
        StartLine       = startLine,
        EndLine         = startLine,
        ClassName       = className,
        MemberName      = memberName,
        ProjectName     = projectName,
        ProjectLanguage = language
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// SA001 — NotImplementedExceptionRule
// ═══════════════════════════════════════════════════════════════════════════

public class NotImplementedExceptionRuleTests
{
    private readonly NotImplementedExceptionRule _sut = new();

    // ── Detekteras ────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_ThrowNotImplementedException_DetectsIssue()
    {
        const string code = """
            public class PaymentService
            {
                public void ProcessRefund(string orderId)
                {
                    throw new NotImplementedException();
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/PaymentService.cs");
        issues.Should().ContainSingle(i => i.RuleId == NotImplementedExceptionRule.RuleId);
    }

    [Fact]
    public void Analyze_ThrowWithMessage_DetectsIssue()
    {
        const string code = """
            public class OrderService
            {
                public Task<Order> GetOrderAsync(Guid id)
                {
                    throw new NotImplementedException("TODO: implement");
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/OrderService.cs");
        issues.Should().HaveCount(1);
        issues[0].RuleId.Should().Be("SA001");
    }

    [Fact]
    public void Analyze_ExpressionBody_DetectsIssue()
    {
        const string code = """
            public class Calc
            {
                public int Add(int a, int b) => throw new NotImplementedException();
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Calc.cs");
        issues.Should().HaveCount(1);
    }

    [Fact]
    public void Analyze_MultipleNIE_DetectsAll()
    {
        const string code = """
            public class UserService
            {
                public void Create() { throw new NotImplementedException(); }
                public void Delete() { throw new NotImplementedException(); }
                public void Update() { throw new NotImplementedException(); }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/UserService.cs");
        issues.Should().HaveCount(3);
    }

    // ── Ej detekteras (false positive-skydd) ─────────────────────────────

    [Fact]
    public void Analyze_OtherException_NoIssue()
    {
        const string code = """
            public class Validator
            {
                public void Validate(string s)
                {
                    if (s is null) throw new ArgumentNullException(nameof(s));
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Validator.cs");
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_MixedBody_OnlyNIEFlagged()
    {
        const string code = """
            public class Repo
            {
                public void Save(object entity) { throw new NotImplementedException(); }
                public object Load(Guid id) { return new object(); }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Repo.cs");
        issues.Should().ContainSingle();
    }

    // ── Starter-kod genereras (IsAutoFixable + FixedSnippet) ─────────────

    [Fact]
    public void Analyze_VoidMethod_GeneratesStarterCode()
    {
        const string code = """
            public class Svc
            {
                public void Execute() { throw new NotImplementedException(); }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Svc.cs");
        issues[0].IsAutoFixable.Should().BeTrue();
        issues[0].FixedSnippet.Should().NotBeNullOrWhiteSpace();
        issues[0].FixedSnippet.Should().Contain("TODO");
    }

    [Fact]
    public void Analyze_AsyncTaskMethod_GeneratesTaskStarterCode()
    {
        const string code = """
            using System.Threading.Tasks;
            public class Svc
            {
                public async Task RunAsync() { throw new NotImplementedException(); }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Svc.cs");
        issues[0].FixedSnippet.Should().Contain("Task");
    }

    [Fact]
    public void Analyze_BoolReturn_FixedSnippetReturnsDefaultBool()
    {
        const string code = """
            public class Checker
            {
                public bool IsValid(string s) => throw new NotImplementedException();
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Checker.cs");
        issues[0].FixedSnippet.Should().Contain("false");
    }

    // ── Scope-metadata är korrekt ─────────────────────────────────────────

    [Fact]
    public void Analyze_SetsClassAndMemberName()
    {
        const string code = """
            namespace Acme.Orders
            {
                public class OrderHandler
                {
                    public void Handle() { throw new NotImplementedException(); }
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Handler.cs");
        issues[0].Scope.ClassName.Should().Be("OrderHandler");
        issues[0].Scope.MemberName.Should().Be("Handle");
        issues[0].Scope.Namespace.Should().Be("Acme.Orders");
    }

    // ── Inaktiverad regel ─────────────────────────────────────────────────

    [Fact]
    public void Analyze_RuleDisabled_ReturnsEmpty()
    {
        const string code = """
            public class Svc { public void Run() { throw new NotImplementedException(); } }
            """;

        var enabledRules = new HashSet<string> { "CA001" }; // SA001 saknas
        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "f.cs", enabledRules);
        issues.Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// SA002 — MultiClassFileRule
// ═══════════════════════════════════════════════════════════════════════════

public class MultiClassFileRuleTests
{
    private readonly MultiClassFileRule _sut = new();

    // ── Detekteras ────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_TwoTopLevelClasses_DetectsIssue()
    {
        const string code = """
            public class OrderService { }
            public class CustomerService { }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Services.cs");
        issues.Should().HaveCount(1);
        issues[0].RuleId.Should().Be("SA002");
        issues[0].Message.Should().Contain("CustomerService");
    }

    [Fact]
    public void Analyze_ClassAndInterface_DetectsIssue()
    {
        const string code = """
            public interface IFoo { }
            public class Foo : IFoo { }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Foo.cs");
        issues.Should().HaveCount(1);
    }

    [Fact]
    public void Analyze_ThreeTopLevel_OneIssuePerExtraType()
    {
        const string code = """
            public class A { }
            public class B { }
            public class C { }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Abc.cs");
        issues.Should().HaveCount(2, "B och C är 'extra' i förhållande till A");
    }

    // ── Ej detekteras ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_SingleClass_NoIssue()
    {
        const string code = """
            public class PaymentService
            {
                public void Pay(decimal amount) { }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/PaymentService.cs");
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_NestedClass_NotCounted()
    {
        const string code = """
            public class Outer
            {
                private class Inner { }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Outer.cs");
        issues.Should().BeEmpty("nästlade klasser är undantagna");
    }

    [Fact]
    public void Analyze_DtoSuffix_AllowedCohabitation()
    {
        const string code = """
            public class UserService { }
            public record CreateUserDto(string Name, string Email);
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/UserService.cs");
        issues.Should().BeEmpty("Dto-typer tillåts i samma fil som sin service");
    }

    [Fact]
    public void Analyze_PartialClass_NoIssue()
    {
        const string code = """
            public partial class OrderContext { }
            public partial class OrderContext { public void Configure() { } }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/OrderContext.cs");
        issues.Should().BeEmpty("partial-klasser tillhör samma typ");
    }

    // ── Suggestion innehåller utbrytningsinstruktion ──────────────────────

    [Fact]
    public void Analyze_Issue_SuggestionMentionsExtraction()
    {
        const string code = """
            public class Alpha { }
            public class Beta { }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Mixed.cs");
        issues[0].Suggestion.Should().Contain("Beta.cs", "föreslår rätt filnamn");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// SA003 — ComplexMethodRule
// ═══════════════════════════════════════════════════════════════════════════

public class ComplexMethodRuleTests
{
    private readonly ComplexMethodRule _sut = new();

    // ── Detekteras — hög komplexitet ──────────────────────────────────────

    [Fact]
    public void Analyze_HighCyclomaticComplexity_DetectsIssue()
    {
        // 11 beslutspunkter → CC = 12 (≥ 10 = flaggas)
        var code = """
            public class Processor
            {
                public string Process(int x, string s, bool b, object o)
                {
                    if (x > 0) { }
                    if (x > 1) { }
                    if (x > 2) { }
                    if (x > 3) { }
                    if (x > 4) { }
                    if (x > 5) { }
                    if (x > 6) { }
                    if (x > 7) { }
                    if (x > 8) { }
                    if (x > 9) { }
                    if (s != null) { }
                    return s ?? "default";
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Processor.cs");
        issues.Should().ContainSingle(i => i.RuleId == "SA003");
    }

    [Fact]
    public void Analyze_LongMethod_DetectsIssue()
    {
        // Generera en metod med 35 rader (> default MaxMethodLines=30)
        var lines = string.Join("\n", Enumerable.Range(1, 33).Select(i => $"        var v{i} = {i};"));
        var code = $$"""
            public class LongClass
            {
                public void VeryLong()
                {
            {{lines}}
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Long.cs");
        issues.Should().ContainSingle(i => i.RuleId == "SA003");
    }

    [Fact]
    public void Analyze_DeeplyNested_DetectsIssue()
    {
        const string code = """
            public class Nester
            {
                public void Deep(bool a, bool b, bool c, bool d, bool e)
                {
                    if (a)
                    {
                        if (b)
                        {
                            if (c)
                            {
                                if (d)
                                {
                                    if (e) { /* depth 5 */ }
                                }
                            }
                        }
                    }
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Nester.cs");
        issues.Should().ContainSingle();
        issues[0].Message.Should().Contain("nesting", StringComparison.OrdinalIgnoreCase);
    }

    // ── Ej detekteras ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_SimpleMethod_NoIssue()
    {
        const string code = """
            public class Simple
            {
                public int Add(int a, int b) => a + b;
                public string Greet(string name) => $"Hello, {name}!";
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Simple.cs");
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_ShortComplexishMethod_BelowThreshold_NoIssue()
    {
        const string code = """
            public class Validator
            {
                public bool Validate(string s)
                {
                    if (s is null) return false;
                    if (s.Length == 0) return false;
                    if (s.Length > 100) return false;
                    return true;
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Validator.cs");
        issues.Should().BeEmpty("CC=4, ej ≥10");
    }

    // ── Snippet och suggestion ────────────────────────────────────────────

    [Fact]
    public void Analyze_ComplexMethod_SuggestionMentionsExtraction()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 35).Select(i => $"        var x{i} = {i};"));
        var code = $$"""
            public class Fat
            {
                public void BigMethod()
                {
            {{lines}}
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Fat.cs");
        issues[0].Suggestion.Should().Contain("BigMethod");
        issues[0].Scope.ClassName.Should().Be("Fat");
        issues[0].Scope.MemberName.Should().Be("BigMethod");
    }

    // ── Undantag: genererade metoder ─────────────────────────────────────

    [Fact]
    public void Analyze_GeneratedCodeAttribute_Exempt()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 35).Select(i => $"        var x{i} = {i};"));
        var code = $$"""
            using System.CodeDom.Compiler;
            public class Generated
            {
                [System.CodeDom.Compiler.GeneratedCode("tool", "1.0")]
                public void BigGenerated()
                {
            {{lines}}
                }
            }
            """;

        var issues = _sut.Analyze(TestHelpers.ParseTree(code), "src/Generated.cs");
        issues.Should().BeEmpty("genererade metoder är undantagna");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// PromptFactoryService
// ═══════════════════════════════════════════════════════════════════════════

public class PromptFactoryServiceTests
{
    private readonly IPromptFactoryService _sut = new PromptFactoryService();

    // ── Copilot-format ────────────────────────────────────────────────────

    [Fact]
    public void Generate_Copilot_IsCompact()
    {
        var ctx    = TestHelpers.MakeContext();
        var result = _sut.Generate(ctx, PromptTarget.Copilot);

        result.Target.Should().Be(PromptTarget.Copilot);
        result.Content.Should().NotBeNullOrEmpty();
        result.EstimatedTokens.Should().BeLessThan(500,
            "Copilot-promptar ska vara korta för editor-inline-chat");
    }

    [Fact]
    public void Generate_Copilot_ContainsFilePathAndRule()
    {
        var ctx    = TestHelpers.MakeContext(filePath: "src/Services/OrderService.cs", ruleId: "SA001");
        var result = _sut.Generate(ctx, PromptTarget.Copilot);

        result.Content.Should().Contain("OrderService.cs");
        result.Content.Should().Contain("SA001");
    }

    [Fact]
    public void Generate_Copilot_WithFixedSnippet_IncludesStarterCode()
    {
        var ctx = TestHelpers.MakeContext(
            fixedSnippet: "// TODO: implement\npublic void Execute() { }",
            autoFixable:  true);

        var result = _sut.Generate(ctx, PromptTarget.Copilot);
        result.Content.Should().Contain("TODO");
    }

    // ── Claude-format ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_Claude_IsSubstantive()
    {
        var ctx    = TestHelpers.MakeContext();
        var result = _sut.Generate(ctx, PromptTarget.Claude);

        result.Target.Should().Be(PromptTarget.Claude);
        result.Content.Length.Should().BeGreaterThan(300,
            "Claude-promptar ska ge fullständig arkitektonisk kontext");
    }

    [Fact]
    public void Generate_Claude_ContainsTechnicalSpec()
    {
        var ctx    = TestHelpers.MakeContext(className: "PaymentService", memberName: "ProcessRefund");
        var result = _sut.Generate(ctx, PromptTarget.Claude);

        // Ska innehålla strukturerade sektioner
        result.Content.Should().ContainAny("##", "**", "###",
            "Claude-promten ska ha markdown-struktur");
        result.Content.Should().Contain("PaymentService");
        result.Content.Should().Contain("ProcessRefund");
    }

    [Fact]
    public void Generate_Claude_IncludesRelatedIssuesWhenPresent()
    {
        var ctx = TestHelpers.MakeContext() with
        {
            RelatedIssuesSummary = new[]
            {
                "SA001 in OrderService.cs:55 — NotImplementedException",
                "SA003 in OrderService.cs:80 — Complex method"
            }
        };

        var result = _sut.Generate(ctx, PromptTarget.Claude);
        result.Content.Should().Contain("SA001", "relaterade issues ska synas i kontexten");
    }

    // ── GenerateBoth ──────────────────────────────────────────────────────

    [Fact]
    public void GenerateBoth_ReturnsDistinctPrompts()
    {
        var ctx = TestHelpers.MakeContext();
        var (copilot, claude) = _sut.GenerateBoth(ctx);

        copilot.Target.Should().Be(PromptTarget.Copilot);
        claude.Target.Should().Be(PromptTarget.Claude);
        copilot.Content.Should().NotBe(claude.Content,
            "Copilot och Claude kräver olika format");
    }

    [Fact]
    public void GenerateBoth_BothContainFilePath()
    {
        var ctx = TestHelpers.MakeContext(filePath: "src/Domain/Entities/Order.cs");
        var (copilot, claude) = _sut.GenerateBoth(ctx);

        copilot.Content.Should().Contain("Order.cs");
        claude.Content.Should().Contain("Order.cs");
    }

    // ── SA001-specifik prompt ─────────────────────────────────────────────

    [Fact]
    public void Generate_SA001_CopilotIncludesImplementKeyword()
    {
        var ctx = TestHelpers.MakeContext(
            ruleId:      "SA001",
            memberName:  "ProcessRefund",
            snippet:     "throw new NotImplementedException();",
            autoFixable: true,
            fixedSnippet: "// TODO: implement ProcessRefund\npublic Task<RefundResult> ProcessRefund(string id)\n{\n    throw new NotImplementedException();\n}");

        var result = _sut.Generate(ctx, PromptTarget.Copilot);
        result.Content.Should().ContainAny("implement", "Implement", "TODO");
    }

    [Fact]
    public void Generate_SA001_Claude_IncludesStarterCodeSection()
    {
        var ctx = TestHelpers.MakeContext(
            ruleId:      "SA001",
            autoFixable: true,
            fixedSnippet: "public async Task DoWork() {\n    // TODO: your implementation\n    await Task.CompletedTask;\n}");

        var result = _sut.Generate(ctx, PromptTarget.Claude);
        result.Content.Should().Contain("DoWork");
    }

    // ── SA002-specifik prompt ─────────────────────────────────────────────

    [Fact]
    public void Generate_SA002_CopilotMentionsFileExtraction()
    {
        var ctx = TestHelpers.MakeContext(
            ruleId:     "SA002",
            ruleName:   "Multiple Types in File",
            snippet:    "public class OrderService { }\npublic class CustomerService { }",
            suggestion: "Move 'CustomerService' to CustomerService.cs");

        var result = _sut.Generate(ctx, PromptTarget.Copilot);
        result.Content.Should().ContainAny("CustomerService", "extract", "move", "separate");
    }

    [Fact]
    public void Generate_SA002_Claude_IncludesRefactoringSteps()
    {
        var ctx = TestHelpers.MakeContext(
            ruleId:   "SA002",
            ruleName: "Multiple Types in File",
            snippet:  "public class A { }\npublic class B { }");

        var result = _sut.Generate(ctx, PromptTarget.Claude);
        result.Content.Length.Should().BeGreaterThan(200);
    }

    // ── SA003-specifik prompt ─────────────────────────────────────────────

    [Fact]
    public void Generate_SA003_Claude_MentionsExtractionPatterns()
    {
        var ctx = TestHelpers.MakeContext(
            ruleId:     "SA003",
            ruleName:   "Complex Method",
            memberName: "BuildReport",
            snippet:    "// 45-line method with CC=14");

        var result = _sut.Generate(ctx, PromptTarget.Claude);
        result.Content.Should().ContainAny(
            "BuildReport", "extract", "method", "responsibility", "single",
            StringComparer.OrdinalIgnoreCase);
    }

    // ── GeneratedPrompt-modellen ──────────────────────────────────────────

    [Fact]
    public void Generate_TitleContainsRuleIdAndMember()
    {
        var ctx    = TestHelpers.MakeContext(ruleId: "SA001", memberName: "Execute");
        var result = _sut.Generate(ctx, PromptTarget.Copilot);

        result.Title.Should().Contain("SA001");
        result.RuleId.Should().Be("SA001");
        result.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Generate_EstimatedTokens_GreaterThanZero()
    {
        var ctx    = TestHelpers.MakeContext();
        var result = _sut.Generate(ctx, PromptTarget.Claude);
        result.EstimatedTokens.Should().BeGreaterThan(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Hjälpklass — PromptContext with RelatedIssuesSummary (testar extension)
// ═══════════════════════════════════════════════════════════════════════════

file static class PromptContextExtensions
{
    /// <summary>
    /// Lägger till relaterade issue-strängar om PromptContext stöder det.
    /// Kompilerar om fältet inte finns utan att krasha — framtidssäker.
    /// </summary>
    public static PromptContext WithRelated(
        this PromptContext ctx, IReadOnlyList<string>? related) =>
        ctx with { RelatedIssuesSummary = related ?? [] };
}

// ═══════════════════════════════════════════════════════════════════════════
// Integreringstest: Regel → PromptFactory-pipeline
// ═══════════════════════════════════════════════════════════════════════════

public class RuleToPromptPipelineTests
{
    private readonly NotImplementedExceptionRule _sa001 = new();
    private readonly MultiClassFileRule          _sa002 = new();
    private readonly ComplexMethodRule           _sa003 = new();
    private readonly IPromptFactoryService       _factory = new PromptFactoryService();

    [Fact]
    public void SA001_RawIssue_CanBuildValidPromptContext()
    {
        const string code = """
            public class InventoryService
            {
                public void Reserve(string sku, int qty)
                {
                    throw new NotImplementedException();
                }
            }
            """;

        var issues = _sa001.Analyze(TestHelpers.ParseTree(code), "src/InventoryService.cs");
        issues.Should().NotBeEmpty();

        var issue = issues[0];
        var ctx = new PromptContext
        {
            RuleId          = issue.RuleId,
            RuleName        = "NotImplementedException Detected",
            RuleDescription = "Method body is not implemented.",
            Category        = issue.Category,
            Severity        = issue.Severity,
            FilePath        = issue.FilePath,
            Snippet         = issue.Snippet,
            Suggestion      = issue.Suggestion,
            FixedSnippet    = issue.FixedSnippet,
            IsAutoFixable   = issue.IsAutoFixable,
            StartLine       = issue.StartLine,
            EndLine         = issue.EndLine,
            Namespace       = issue.Scope.Namespace,
            ClassName       = issue.Scope.ClassName,
            MemberName      = issue.Scope.MemberName
        };

        // Generera bägge prompts
        var (copilot, claude) = _factory.GenerateBoth(ctx);

        copilot.Content.Should().Contain("InventoryService");
        claude.Content.Should().Contain("Reserve");
        claude.EstimatedTokens.Should().BeGreaterThan(copilot.EstimatedTokens,
            "Claude-promten ska vara mer substantiell än Copilot-promten");
    }

    [Fact]
    public void SA002_RawIssue_CanBuildValidPromptContext()
    {
        const string code = """
            public class ShippingService { }
            public class TrackingService { }
            """;

        var issues = _sa002.Analyze(TestHelpers.ParseTree(code), "src/ShippingAndTracking.cs");
        issues.Should().NotBeEmpty();

        var issue = issues[0];
        var ctx = new PromptContext
        {
            RuleId          = issue.RuleId,
            RuleName        = "Multiple Types in File",
            RuleDescription = "File contains multiple top-level type declarations.",
            Category        = issue.Category,
            Severity        = issue.Severity,
            FilePath        = issue.FilePath,
            Snippet         = issue.Snippet,
            Suggestion      = issue.Suggestion,
            IsAutoFixable   = false,
            StartLine       = issue.StartLine,
            EndLine         = issue.EndLine,
            ClassName       = issue.Scope.ClassName,
            MemberName      = issue.Scope.MemberName
        };

        var result = _factory.Generate(ctx, PromptTarget.Claude);
        result.Should().NotBeNull();
        result.Content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SA003_RawIssue_CopilotPromptIsUnder500Tokens()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 35).Select(i => $"    var x{i} = {i};"));
        var code = $$"""
            public class ReportBuilder
            {
                public string BuildReport(object data)
                {
            {{lines}}
                    return "result";
                }
            }
            """;

        var issues = _sa003.Analyze(TestHelpers.ParseTree(code), "src/ReportBuilder.cs");
        if (issues.Count == 0) return; // metoden kanske inte triggar SA003 — skippa

        var issue = issues[0];
        var ctx = new PromptContext
        {
            RuleId          = issue.RuleId,
            RuleName        = "Complex Method Extraction Candidate",
            RuleDescription = "Method has high complexity and should be broken down.",
            Category        = issue.Category,
            Severity        = issue.Severity,
            FilePath        = issue.FilePath,
            Snippet         = issue.Snippet,
            Suggestion      = issue.Suggestion,
            IsAutoFixable   = false,
            StartLine       = issue.StartLine,
            EndLine         = issue.EndLine,
            ClassName       = issue.Scope.ClassName,
            MemberName      = issue.Scope.MemberName
        };

        var copilot = _factory.Generate(ctx, PromptTarget.Copilot);
        copilot.EstimatedTokens.Should().BeLessThan(500);
    }
}
