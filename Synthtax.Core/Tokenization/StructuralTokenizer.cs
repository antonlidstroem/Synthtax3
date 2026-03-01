using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Synthtax.Core.Tokenization;

// ═══════════════════════════════════════════════════════════════════════════
// TokenAlphabet  —  vad varje symbol normaliseras till
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Platshållare som ersätter semantiskt icke-relevanta tokens.
/// Används för att bevara strukturell signatur utan att binda fingerprinting
/// till specifika variabelnamn.
/// </summary>
public static class TokenAlphabet
{
    public const string Identifier = "$I";   // variabel/metod/fält-namn
    public const string StringLit  = "$S";   // "text" / 'text'
    public const string NumberLit  = "$N";   // 42, 3.14, 0xFF
    public const string TypeRef    = "$T";   // MyClass, List<T>   (PascalCase ej keyword)
    public const string Unknown    = "$?";
}

// ═══════════════════════════════════════════════════════════════════════════
// StructuralTokenizer
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Omvandlar ett råt kodsnippet till en *strukturell tokensträng* — en kompakt
/// representation av kodens logiska form utan implementationsdetaljer.
///
/// <para><b>Pipeline:</b>
/// <list type="number">
///   <item>Strip kommentarer (återanvänder <c>SnippetNormalizer</c>).</item>
///   <item>Tokenisera med regex: strängliteraler → $S, nummerliteraler → $N,
///         keywords → bevarade, identifierare → $I eller $T.</item>
///   <item>Normalisera whitespace till enkla mellanslag.</item>
///   <item>Returnera tokensträngen, t.ex. <c>"if ( $I != null ) { return $S ; }"</c>.</item>
/// </list>
/// </para>
///
/// <para><b>Exempel:</b>
/// <code>
///   Input:   "if (userName != null) { throw new ArgumentNullException(nameof(userName)); }"
///   Output:  "IF ( $I != NULL ) { THROW NEW $T ( NAMEOF ( $I ) ) ; }"
/// </code>
/// Både <c>userName</c> och <c>email</c> ger identisk tokensträng — samma strukturella mönster.
/// </para>
/// </summary>
public sealed class StructuralTokenizer
{
    // ── Förcompilerade mönster ────────────────────────────────────────────

    // Strängliteraler (C/Java/Python varianter)
    private static readonly Regex StringLiteralRx = new(
        @"(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`[^`]*`)",
        RegexOptions.Compiled);

    // Nummerliteraler: hex, float, int
    private static readonly Regex NumberLiteralRx = new(
        @"\b(0[xX][0-9a-fA-F]+[uUlL]*|[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?[fFdDmMlLuU]*)\b",
        RegexOptions.Compiled);

    // Identifierare: börjar med letter/_/$, kan innehålla siffror
    private static readonly Regex IdentifierRx = new(
        @"\b([a-zA-Z_$][a-zA-Z0-9_$]*)\b",
        RegexOptions.Compiled);

    // Operators och punctuation som ska bevaras verbatim
    private static readonly Regex OperatorRx = new(
        @"(=>|->|::|==|!=|<=|>=|&&|\|\||<<|>>|\+\+|--|[+\-*/%&|^~!<>=?:;,.()\[\]{}])",
        RegexOptions.Compiled);

    // Whitespace-kollaps
    private static readonly Regex WhitespaceRx = new(
        @"\s+", RegexOptions.Compiled);

    // ── Keyword-mängder (language-agnostika — union av C#/Java/Python/JS) ──

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kontrollflöde
        "if", "else", "switch", "case", "default", "for", "foreach", "while", "do",
        "break", "continue", "return", "yield", "goto", "throw", "try", "catch",
        "finally", "using", "lock", "checked", "unchecked", "unsafe", "fixed",
        // Typdeklarationer
        "class", "struct", "interface", "enum", "record", "delegate",
        "namespace", "abstract", "sealed", "static", "partial", "readonly",
        "override", "virtual", "extern", "new", "base", "this", "typeof", "sizeof",
        "nameof", "is", "as", "in", "out", "ref",
        // Synlighet
        "public", "private", "protected", "internal",
        // Typer (inbyggda)
        "void", "bool", "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal", "char", "string", "object",
        "var", "dynamic",
        // Värden
        "null", "true", "false",
        // Java
        "extends", "implements", "final", "synchronized", "volatile", "transient",
        "instanceof", "throws", "import", "package",
        // Python
        "def", "lambda", "pass", "with", "assert", "del", "not", "and", "or",
        "raise", "except", "from", "global", "nonlocal", "async", "await",
        "elif", "print",
        // JavaScript/TypeScript
        "function", "let", "const", "typeof", "instanceof", "export", "import",
        "extends", "super",
    };

    // Typer med PascalCase som förekommer i generiska positioner
    // — dessa är ofta API-typer, inte user-identifierare → normalisera till $T
    private static readonly HashSet<string> KnownFrameworkTypes = new(StringComparer.Ordinal)
    {
        "List", "Dictionary", "IEnumerable", "IReadOnlyList", "HashSet", "Queue",
        "Stack", "Array", "Task", "ValueTask", "Nullable", "Optional", "Result",
        "Exception", "ArgumentException", "NullReferenceException",
        "ILogger", "IServiceProvider", "CancellationToken", "Guid", "DateTime",
        "TimeSpan", "Uri", "Stream", "StringBuilder", "Regex",
    };

    // ── Publik API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Tokeniserar ett kodsnippet och returnerar den strukturella tokensträngen.
    /// </summary>
    /// <param name="snippet">Råt kodsnippet (ett stycke kod).</param>
    /// <param name="fileExtension">Används för att välja kommentarssyntax.</param>
    public string Tokenize(string snippet, string? fileExtension = null)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return string.Empty;

        var s = snippet;

        // 1. Ersätt strängliteraler INNAN annan behandling (undviker att parsa deras innehåll)
        s = StringLiteralRx.Replace(s, " " + TokenAlphabet.StringLit + " ");

        // 2. Ersätt nummerliteraler
        s = NumberLiteralRx.Replace(s, " " + TokenAlphabet.NumberLit + " ");

        // 3. Tokenisera identifierare och keywords
        s = IdentifierRx.Replace(s, m => ClassifyIdentifier(m.Value));

        // 4. Normalisera whitespace
        s = WhitespaceRx.Replace(s, " ").Trim();

        return s;
    }

    /// <summary>
    /// Returnerar tokensträngen som en lista av tokens för n-gram-generering.
    /// </summary>
    public IReadOnlyList<string> TokenizeToList(string snippet, string? fileExtension = null)
    {
        var str = Tokenize(snippet, fileExtension);
        if (string.IsNullOrEmpty(str)) return [];

        return str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    // ── Privata hjälpmetoder ──────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ClassifyIdentifier(string token)
    {
        // 1. Keyword → bevara, uppercase för kultur-invarians
        if (Keywords.Contains(token))
            return token.ToUpperInvariant();

        // 2. Känd framework-typ (PascalCase) → $T
        if (KnownFrameworkTypes.Contains(token))
            return TokenAlphabet.TypeRef;

        // 3. PascalCase och längre än 1 tecken → trolig typ/klass → $T
        if (token.Length > 1 && char.IsUpper(token[0]))
            return TokenAlphabet.TypeRef;

        // 4. ALL_CAPS (konstant) → $N (behandla som literalvärde)
        if (token.Length > 1 && token.All(c => char.IsUpper(c) || c == '_'))
            return TokenAlphabet.NumberLit;

        // 5. Övrigt identifierare (camelCase, underscore_case) → $I
        return TokenAlphabet.Identifier;
    }
}
