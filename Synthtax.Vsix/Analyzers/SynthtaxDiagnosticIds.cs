using Microsoft.CodeAnalysis;

namespace Synthtax.Vsix.Analyzers;

/// <summary>
/// Centrala DiagnosticDescriptor-instanser för alla Synthtax-regler.
///
/// <para>ID-konvention: SX + 4-siffrigt löpnummer.
/// Speglar Synthtax API:s RuleId-konvention (SA001→SX0001, CA001→SX1001, …).</para>
/// </summary>
internal static class SynthtaxDiagnosticIds
{
    private const string Category     = "Synthtax";
    private const string HelpBaseUri  = "https://synthtax.io/rules/";

    // ── SA001: NotImplementedException ────────────────────────────────────
    public static readonly DiagnosticDescriptor SA001_NotImplemented = new(
        id:                 "SX0001",
        title:              "NotImplementedException detected",
        messageFormat:      "Method '{0}' in '{1}' throws NotImplementedException — starter code available",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "A method body is not implemented. Use the lightbulb (💡) to generate starter code via Copilot or Claude.",
        helpLinkUri:        HelpBaseUri + "SA001");

    // ── SA002: Multiple types in file ────────────────────────────────────
    public static readonly DiagnosticDescriptor SA002_MultipleTypes = new(
        id:                 "SX0002",
        title:              "Multiple type declarations in single file",
        messageFormat:      "File contains {0} top-level type declarations — extract '{1}' to its own file",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:        "Placing multiple top-level types in one file reduces navigability. Extract each type to its own file.",
        helpLinkUri:        HelpBaseUri + "SA002");

    // ── SA003: Complex method ────────────────────────────────────────────
    public static readonly DiagnosticDescriptor SA003_ComplexMethod = new(
        id:                 "SX0003",
        title:              "Complex method — extraction candidate",
        messageFormat:      "Method '{0}' has cyclomatic complexity {1} (threshold: {2}) — consider extracting helper methods",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "High cyclomatic complexity indicates a method that does too much. Break it into smaller, focused methods.",
        helpLinkUri:        HelpBaseUri + "SA003");

    // ── Generisk: issues från Synthtax API (Critical/High) ───────────────
    public static readonly DiagnosticDescriptor SXGenericCritical = new(
        id:                 "SX9001",
        title:              "Synthtax Critical Issue",
        messageFormat:      "[{0}] {1}",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description:        "A critical code quality issue detected by Synthtax analysis.",
        helpLinkUri:        HelpBaseUri + "api-issues");

    public static readonly DiagnosticDescriptor SXGenericHigh = new(
        id:                 "SX9002",
        title:              "Synthtax High-Severity Issue",
        messageFormat:      "[{0}] {1}",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:        "A high-severity code quality issue detected by Synthtax analysis.",
        helpLinkUri:        HelpBaseUri + "api-issues");

    public static readonly DiagnosticDescriptor SXGenericMedium = new(
        id:                 "SX9003",
        title:              "Synthtax Issue",
        messageFormat:      "[{0}] {1}",
        category:           Category,
        defaultSeverity:    DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description:        "A code quality issue detected by Synthtax analysis.",
        helpLinkUri:        HelpBaseUri + "api-issues");

    // Mapping: Synthtax Severity string → DiagnosticDescriptor
    public static DiagnosticDescriptor ForSeverity(string severity) => severity switch
    {
        "Critical" => SXGenericCritical,
        "High"     => SXGenericHigh,
        _          => SXGenericMedium
    };

    // Mapping: Synthtax RuleId → specifik descriptor om tillgänglig
    public static DiagnosticDescriptor ForRuleId(string ruleId, string severity) => ruleId switch
    {
        "SA001" => SA001_NotImplemented,
        "SA002" => SA002_MultipleTypes,
        "SA003" => SA003_ComplexMethod,
        _       => ForSeverity(severity)
    };
}
