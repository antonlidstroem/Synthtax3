// Synthtax.Tests/Core/FingerprintServiceTests.cs
// Kräver: xunit, FluentAssertions
// dotnet add package xunit FluentAssertions

using FluentAssertions;
using Synthtax.Core.Contracts;
using Synthtax.Core.Fingerprinting;
using Synthtax.Core.Normalization;
using NSubstitute;

namespace Synthtax.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// FingerprintService — enhetstester
// ═══════════════════════════════════════════════════════════════════════════

public class FingerprintServiceTests
{
    private readonly IFingerprintService _sut = new FingerprintService();
    private static readonly Guid ProjectId    = new("11111111-1111-1111-1111-111111111111");

    // ── Grundläggande korrekthet ───────────────────────────────────────────

    [Fact]
    public void Compute_ReturnsFixed64CharHexString()
    {
        var input = ValidInput("CODE = 1");
        var hash  = _sut.Compute(input);

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$", "SHA-256 hex ska vara lowercase");
    }

    [Fact]
    public void Compute_SameInput_ReturnsSameHash()
    {
        var input = ValidInput("var x = null;");
        _sut.Compute(input).Should().Be(_sut.Compute(input));
    }

    [Fact]
    public void Compute_DifferentRuleId_ReturnsDifferentHash()
    {
        var a = _sut.Compute(ValidInput("CODE") with { RuleId = "CA001" });
        var b = _sut.Compute(ValidInput("CODE") with { RuleId = "CA002" });
        a.Should().NotBe(b);
    }

    [Fact]
    public void Compute_DifferentProject_ReturnsDifferentHash()
    {
        var a = _sut.Compute(ValidInput("CODE"));
        var b = _sut.Compute(ValidInput("CODE") with
        {
            ProjectId = new Guid("22222222-2222-2222-2222-222222222222")
        });
        a.Should().NotBe(b);
    }

    // ── Stabilitet — trivia ska inte påverka fingerprint ──────────────────

    [Theory]
    [InlineData("var x = null;",       "var x = null; // saknar null-check")]
    [InlineData("var x = null;",       "/* legacy */ var x = null;")]
    [InlineData("var x = null;",       "var  x  =  null ;")]  // extra whitespace
    [InlineData("var x = null;",       "\t\tvar x = null;\n")]  // tabs och newlines
    public void Compute_TriviaVariations_ReturnSameHash(string clean, string withTrivia)
    {
        var hash1 = _sut.Compute(ValidInput(clean));
        var hash2 = _sut.Compute(ValidInput(withTrivia));
        hash1.Should().Be(hash2, $"trivia ska inte påverka fingerprint: '{clean}' vs '{withTrivia}'");
    }

    // ── Culture-Invariant Case Normalization ──────────────────────────────

    [Theory]
    [InlineData("var x = NULL;", "var x = null;")]
    [InlineData("VAR X = NULL;", "var x = null;")]
    [InlineData("Var X = Null;", "var x = null;")]
    public void Compute_CaseVariations_ReturnSameHash(string upper, string lower)
    {
        var hash1 = _sut.Compute(ValidInput(upper));
        var hash2 = _sut.Compute(ValidInput(lower));
        hash1.Should().Be(hash2, "case-normalisering ska vara kultur-invariant");
    }

    // ── Scope-stabilitet ──────────────────────────────────────────────────

    [Fact]
    public void Compute_SameCodeDifferentScope_ReturnsDifferentHash()
    {
        var scopeA = LogicalScope.ForMethod("Acme", "Foo", "MethodA");
        var scopeB = LogicalScope.ForMethod("Acme", "Foo", "MethodB");

        var a = _sut.Compute(ValidInput("CODE") with { Scope = scopeA });
        var b = _sut.Compute(ValidInput("CODE") with { Scope = scopeB });
        a.Should().NotBe(b, "olika metodnamn ska ge olika fingerprint");
    }

    [Fact]
    public void Compute_SameScopeCaseInsensitive_ReturnsSameHash()
    {
        var scopeLower = LogicalScope.ForMethod("acme.payments", "paymentservice", "processrefund");
        var scopeUpper = LogicalScope.ForMethod("ACME.PAYMENTS", "PAYMENTSERVICE", "PROCESSREFUND");

        var a = _sut.Compute(ValidInput("CODE") with { Scope = scopeLower });
        var b = _sut.Compute(ValidInput("CODE") with { Scope = scopeUpper });
        a.Should().Be(b, "scope-jämförelse är kultur-invariant case-insensitiv");
    }

    // ── Validering ────────────────────────────────────────────────────────

    [Fact]
    public void Compute_EmptyProjectId_Throws()
    {
        var act = () => _sut.Compute(ValidInput("CODE") with { ProjectId = Guid.Empty });
        act.Should().Throw<ArgumentException>().WithMessage("*ProjectId*");
    }

    [Fact]
    public void Compute_EmptyRuleId_Throws()
    {
        var act = () => _sut.Compute(ValidInput("CODE") with { RuleId = "  " });
        act.Should().Throw<ArgumentException>().WithMessage("*RuleId*");
    }

    [Fact]
    public void Compute_NullSnippet_Throws()
    {
        var act = () => _sut.Compute(ValidInput("CODE") with { RawSnippet = null! });
        act.Should().Throw<ArgumentNullException>().WithMessage("*RawSnippet*");
    }

    [Fact]
    public void Compute_EmptySnippet_ReturnsHash()
    {
        // Tom snippet är tillåtet — t.ex. när en hel klass flaggas
        var hash = _sut.Compute(ValidInput(""));
        hash.Should().HaveLength(64);
    }

    // ── Batch ─────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeBatch_ReturnsCorrectOrderAndCount()
    {
        var inputs  = Enumerable.Range(1, 10)
            .Select(i => ValidInput($"CODE{i}") with { RuleId = $"CA{i:D3}" })
            .ToList()
            .AsReadOnly();

        var batch  = _sut.ComputeBatch(inputs);
        var single = inputs.Select(i => _sut.Compute(i)).ToList();

        batch.Should().HaveCount(10);
        batch.Should().BeEquivalentTo(single, o => o.WithStrictOrdering());
    }

    // ── Diagnostik ────────────────────────────────────────────────────────

    [Fact]
    public void ComputeWithDiagnostics_ShowsNormalization()
    {
        var (hash, normalized, preHashKey) = _sut.ComputeWithDiagnostics(
            ValidInput("var x = null; // comment"));

        normalized.Should().NotContain("//", "kommentar ska vara borttagen");
        normalized.Should().Be(normalized.ToUpperInvariant(), "ska vara uppercase");
        preHashKey.Should().Contain("PROJECT::");
        preHashKey.Should().Contain("RULE::");
        preHashKey.Should().Contain("SCOPE::");
        preHashKey.Should().Contain("SNIPPET::");
        hash.Should().HaveLength(64);
    }

    // ── Hjälpmetod ────────────────────────────────────────────────────────

    private static FingerprintInput ValidInput(string snippet) => new()
    {
        ProjectId  = ProjectId,
        RuleId     = "CA001",
        Scope      = LogicalScope.ForMethod("Acme.App", "MyService", "DoWork"),
        RawSnippet = snippet,
        FileExtension = ".cs"
    };
}

// ═══════════════════════════════════════════════════════════════════════════
// SnippetNormalizer — enhetstester
// ═══════════════════════════════════════════════════════════════════════════

public class SnippetNormalizerTests
{
    // ── C-stil kommentarer ────────────────────────────────────────────────

    [Theory]
    [InlineData("var x = 1; // remove me", "VAR X = 1;")]
    [InlineData("// full line comment\nvar x = 1;", "VAR X = 1;")]
    [InlineData("var x = /* inline */ 1;", "VAR X = 1;")]
    [InlineData("/** javadoc */ void Foo() {}", "VOID FOO() {}")]
    public void CStyle_StripsComments(string input, string expected)
    {
        var result = SnippetNormalizer.Normalize(input,
            NormalizationOptions.ForFingerprinting,
            CommentStyle.CStyle);
        result.Should().Be(expected);
    }

    // ── Hash-stil kommentarer (Python/YAML) ───────────────────────────────

    [Theory]
    [InlineData("x = 1  # comment", "X = 1")]
    [InlineData("# full line\nx = 1", "X = 1")]
    public void HashStyle_StripsComments(string input, string expected)
    {
        var result = SnippetNormalizer.Normalize(input,
            NormalizationOptions.ForFingerprinting,
            CommentStyle.HashStyle);
        result.Should().Be(expected);
    }

    // ── Whitespace-kollaps ────────────────────────────────────────────────

    [Theory]
    [InlineData("a  b",    "A B")]
    [InlineData("a\tb",    "A B")]
    [InlineData("a\nb",    "A B")]
    [InlineData("a\r\nb",  "A B")]
    [InlineData("  a  ",   "A")]    // trim
    public void CollapseWhitespace_Works(string input, string expected)
    {
        SnippetNormalizer.Normalize(input).Should().Be(expected);
    }

    // ── Culture-Invariant Case ────────────────────────────────────────────

    [Theory]
    [InlineData("null",         "NULL")]
    [InlineData("ToString()",   "TOSTRING()")]
    [InlineData("İstanbul",     "İSTANBUL")] // Turkiska i — invariant ger korrekt result
    public void UpperCaseInvariant_IsApplied(string input, string expected)
    {
        var result = SnippetNormalizer.Normalize(input,
            NormalizationOptions.ForFingerprinting);
        result.Should().Be(expected);
    }

    // ── Trunkering ────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_SnippetLongerThanMaxLength_IsTruncated()
    {
        var long128 = new string('A', 200);
        var result  = SnippetNormalizer.Normalize(long128,
            NormalizationOptions.ForFingerprinting with { MaxLength = 128 });
        result.Should().HaveLength(128);
    }

    // ── Display-profil bevarar kommentarer ────────────────────────────────

    [Fact]
    public void DisplayProfile_KeepsComments()
    {
        var input  = "var x = 1; // keep me";
        var result = SnippetNormalizer.Normalize(input, NormalizationOptions.ForDisplay);
        result.Should().Contain("keep me");
        result.Should().NotBe(result.ToUpper(), "display-profil ändrar inte case");
    }

    // ── Auto-detektering av kommentarssyntax ─────────────────────────────

    [Theory]
    [InlineData(".cs",   "var x = 1; // c-comment",   "VAR X = 1;")]
    [InlineData(".py",   "x = 1  # python-comment",   "X = 1")]
    [InlineData(".java", "int x = 1; // java-comment", "INT X = 1;")]
    public void DetectStyle_CorrectlyIdentifiesLanguage(
        string ext, string input, string expected)
    {
        var result = SnippetNormalizer.Normalize(
            input,
            NormalizationOptions.ForFingerprinting,
            CommentStyle.Auto,
            ext);
        result.Should().Be(expected);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// LogicalScope — enhetstester
// ═══════════════════════════════════════════════════════════════════════════

public class LogicalScopeTests
{
    [Fact]
    public void ToFingerprintKey_IsUpperInvariant()
    {
        var scope = LogicalScope.ForMethod("acme.app", "myService", "doWork");
        scope.ToFingerprintKey().Should().Be(
            scope.ToFingerprintKey().ToUpperInvariant(),
            "fingerprint-nyckel ska vara kultur-invariant uppercase");
    }

    [Fact]
    public void ToFingerprintKey_SameScope_DifferentCase_ProducesSameKey()
    {
        var a = LogicalScope.ForMethod("Acme.App", "MyService", "DoWork");
        var b = LogicalScope.ForMethod("acme.app", "myservice", "dowork");
        a.ToFingerprintKey().Should().Be(b.ToFingerprintKey());
    }

    [Fact]
    public void ToFingerprintKey_ContainsKindSuffix()
    {
        var method = LogicalScope.ForMethod("Ns", "Cls", "Meth");
        method.ToFingerprintKey().Should().EndWith("[METHOD]");

        var cls = LogicalScope.ForClass("Ns", "Cls");
        cls.ToFingerprintKey().Should().EndWith("[CLASS]");
    }

    [Fact]
    public void FileScope_ProducesStableKey()
    {
        var file = LogicalScope.ForFile();
        file.ToFingerprintKey().Should().Be("FILE[FILE]");
    }

    [Fact]
    public void ForMethod_WithNullNamespace_OmitsNamespace()
    {
        var scope = LogicalScope.ForMethod(null, "MyClass", "MyMethod");
        scope.ToFingerprintKey().Should().Be("MYCLASS::MYMETHOD[METHOD]");
    }
}
