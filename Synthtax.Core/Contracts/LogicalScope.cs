namespace Synthtax.Core.Contracts;

// ═══════════════════════════════════════════════════════════════════════════
// LogicalScope
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Semantisk plats för en issue — stabilt alternativ till radnummer.
///
/// <para><b>Varför inte radnummer?</b><br/>
/// Om man lägger till en kommentar på rad 10 förflyttas en bug på rad 50 till rad 51.
/// Det ger ett nytt fingerprint och systemet tror att buggen är ny + den gamla löst.
/// <c>LogicalScope</c> pekar istället på semantiska enheter (metod, klass) som
/// förblir stabila vid radändringar.</para>
///
/// <para><b>Fingerprint-komponent:</b><br/>
/// <c>Acme.Payments.PaymentService::ProcessRefund[Method]</c></para>
/// </summary>
public sealed record LogicalScope
{
    // ── Komponenter ────────────────────────────────────────────────────────

    /// <summary>T.ex. "Acme.Payments" eller "com.example.payments".</summary>
    public string? Namespace  { get; init; }

    /// <summary>T.ex. "PaymentService" (utan namespace-prefix).</summary>
    public string? ClassName  { get; init; }

    /// <summary>T.ex. "ProcessRefund" eller "Amount" (property/field).</summary>
    public string? MemberName { get; init; }

    /// <summary>Exakt typ av semantisk enhet.</summary>
    public ScopeKind Kind { get; init; } = ScopeKind.Unknown;

    // ── Factories ──────────────────────────────────────────────────────────

    /// <summary>Scope på metodnivå.</summary>
    public static LogicalScope ForMethod(string? ns, string? className, string methodName) =>
        new() { Namespace = ns, ClassName = className, MemberName = methodName, Kind = ScopeKind.Method };

    /// <summary>Scope på konstruktornivå.</summary>
    public static LogicalScope ForConstructor(string? ns, string className) =>
        new() { Namespace = ns, ClassName = className, MemberName = ".ctor", Kind = ScopeKind.Constructor };

    /// <summary>Scope på klass-/typnivå (issue gäller hela klassen).</summary>
    public static LogicalScope ForClass(string? ns, string className) =>
        new() { Namespace = ns, ClassName = className, Kind = ScopeKind.Class };

    /// <summary>Scope på property/fält-nivå.</summary>
    public static LogicalScope ForMember(string? ns, string? className, string memberName, ScopeKind kind) =>
        new() { Namespace = ns, ClassName = className, MemberName = memberName, Kind = kind };

    /// <summary>Filnivå-scope — används när det inte går att bestämma semantisk plats.</summary>
    public static LogicalScope ForFile() => new() { Kind = ScopeKind.File };

    /// <summary>Okänt scope — undviks om möjligt (ger sämre fingerprint-stabilitet).</summary>
    public static LogicalScope Unknown => new() { Kind = ScopeKind.Unknown };

    // ── Strängrepresentation ───────────────────────────────────────────────

    /// <summary>
    /// Kanonisk, kultur-invariant sträng för fingerprinting.
    /// Format: <c>ACME.PAYMENTS.PAYMENTSERVICE::PROCESSREFUND[METHOD]</c>
    ///
    /// Versaler (ToUpperInvariant) garanterar att "processRefund" och "ProcessRefund"
    /// ger identiskt fingerprint — kritiskt vid mixade namnkonventioner.
    /// </summary>
    public string ToFingerprintKey()
    {
        var parts = new List<string>(3);

        if (!string.IsNullOrWhiteSpace(Namespace))
            parts.Add(Namespace.Trim());

        if (!string.IsNullOrWhiteSpace(ClassName))
            parts.Add(ClassName.Trim());

        var qualifier = string.Join(".", parts);
        var member    = MemberName?.Trim();

        var result = (qualifier, member) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{qualifier}::{member}",
            ({ Length: > 0 }, _)               => qualifier,
            (_, { Length: > 0 })               => member!,
            _                                  => ScopeKind.File.ToString()
        };

        // Culture-Invariant Case Normalization — Fas 2-krav
        return $"{result.ToUpperInvariant()}[{Kind.ToString().ToUpperInvariant()}]";
    }

    /// <summary>Läsbar representation för UI/logs.</summary>
    public override string ToString()
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(Namespace))  parts.Add(Namespace);
        if (!string.IsNullOrEmpty(ClassName))  parts.Add(ClassName);
        if (!string.IsNullOrEmpty(MemberName)) parts.Add(MemberName);
        return parts.Count > 0
            ? $"{string.Join(".", parts)} [{Kind}]"
            : $"[{Kind}]";
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ScopeKind
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Semantisk typ för <see cref="LogicalScope"/>.</summary>
public enum ScopeKind
{
    Unknown     = 0,
    File        = 1,
    Namespace   = 2,
    Class       = 3,
    Interface   = 4,
    Struct      = 5,
    Enum        = 6,
    Method      = 7,
    Constructor = 8,
    Property    = 9,
    Field       = 10,
    Lambda      = 11,
    Function    = 12,   // Python/JS — top-level function (inte klassmetod)
    Module      = 13    // Python-modul / JS-module
}
