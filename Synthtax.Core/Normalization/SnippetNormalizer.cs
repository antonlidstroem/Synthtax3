using System.Text;
using System.Text.RegularExpressions;

namespace Synthtax.Core.Normalization;

/// <summary>
/// Normaliserar kodsnippets för fingerprinting.
///
/// <para><b>Pipeline (i ordning):</b>
/// <list type="number">
///   <item>Strip trivia: kommentarer beroende på <see cref="CommentStyle"/>.</item>
///   <item>Kollaps whitespace: alla sekvenser av \t, \n, \r, space → ett space.</item>
///   <item>Trim: ledande + avslutande whitespace bort.</item>
///   <item>Culture-Invariant Case: <c>ToUpperInvariant()</c>.</item>
///   <item>Trunkering: Max N tecken.</item>
/// </list>
/// </para>
///
/// <para><b>Trådäkerhet:</b> Alla metoder är statiska och thread-safe.
/// Regex-kompilering sker en gång via static readonly-fält.</para>
/// </summary>
public static class SnippetNormalizer
{
    // ═══════════════════════════════════════════════════════════════════════
    // Förcompilerade reguljära uttryck
    // ═══════════════════════════════════════════════════════════════════════

    // C-stil: /** ... */ (Javadoc/XMLdoc — måste matchas FÖRE /* ... */)
    private static readonly Regex DocCommentRx = new(
        @"/\*\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // C-stil: /* ... */
    private static readonly Regex BlockCommentRx = new(
        @"/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // C-stil: // till end-of-line
    // Hanterar escaped chars: undviker strippning av URL:er i strängar (heuristik)
    private static readonly Regex LineCommentCStyleRx = new(
        @"(?<!:)//[^\n]*",
        RegexOptions.Compiled);

    // Hash-stil: # till end-of-line
    private static readonly Regex LineCommentHashRx = new(
        @"#[^\n]*",
        RegexOptions.Compiled);

    // SQL: -- till end-of-line
    private static readonly Regex LineCommentSqlRx = new(
        @"--[^\n]*",
        RegexOptions.Compiled);

    // HTML/XML: <!-- ... -->
    private static readonly Regex HtmlCommentRx = new(
        @"<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Whitespace: tabs, newlines, carriage returns, multiple spaces → ett space
    private static readonly Regex WhitespaceRx = new(
        @"[\t\r\n ]+",
        RegexOptions.Compiled);

    // Python/C# triple-quote strings  """ ... """
    private static readonly Regex TripleDoubleRx = new(
        @""""""".*?""""""",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Python triple-single  ''' ... '''
    private static readonly Regex TripleSingleRx = new(
        @"'''.*?'''",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // ═══════════════════════════════════════════════════════════════════════
    // Publikt API
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Normaliserar ett snippet med standardinställningar för fingerprinting.
    /// Ekvivalent med <c>Normalize(snippet, NormalizationOptions.ForFingerprinting)</c>.
    /// </summary>
    public static string Normalize(string snippet) =>
        Normalize(snippet, NormalizationOptions.ForFingerprinting);

    /// <summary>
    /// Normaliserar ett snippet med angiven profil och kommentarssyntax.
    /// </summary>
    /// <param name="snippet">Råt kodsnippet.</param>
    /// <param name="options">Normaliseringsprofil.</param>
    /// <param name="commentStyle">
    /// Kommentarssyntax. <see cref="CommentStyle.Auto"/> väljer baserat på
    /// filändelse om <paramref name="fileExtension"/> är angiven.
    /// </param>
    /// <param name="fileExtension">
    /// Filändelse (med eller utan punkt) för automatisk syntaxdetektering.
    /// </param>
    public static string Normalize(
        string             snippet,
        NormalizationOptions options,
        CommentStyle       commentStyle  = CommentStyle.CStyle,
        string?            fileExtension = null)
    {
        if (string.IsNullOrEmpty(snippet)) return string.Empty;

        var style = commentStyle == CommentStyle.Auto && fileExtension is not null
            ? DetectStyle(fileExtension)
            : commentStyle;

        var sb = new StringBuilder(snippet);

        StripComments(sb, style, options);
        if (options.StripTripleQuoteStrings) StripTripleQuotes(sb);
        if (options.CollapseWhitespace)      CollapseWhitespace(sb);

        var result = sb.ToString();
        if (options.TrimEdges)        result = result.Trim();
        if (options.UpperCaseInvariant) result = result.ToUpperInvariant();
        if (options.MaxLength > 0 && result.Length > options.MaxLength)
            result = result[..options.MaxLength];

        return result;
    }

    /// <summary>
    /// Normaliserar en hel fil och returnerar den rad-för-rad.
    /// Kommentarsblock kan spanna flera rader och hanteras korrekt.
    /// </summary>
    public static string NormalizeFile(
        string             content,
        NormalizationOptions options,
        string?            fileExtension = null)
    {
        // För helfilsanalys: strip men behåll radstruktur (kollapsa inte newlines)
        var fileOptions = options with { CollapseWhitespace = false, TrimEdges = false };
        var style       = fileExtension is not null ? DetectStyle(fileExtension) : CommentStyle.CStyle;

        var sb = new StringBuilder(content);
        StripComments(sb, style, fileOptions);
        if (fileOptions.StripTripleQuoteStrings) StripTripleQuotes(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Bestämmer kommentarssyntax baserat på filändelse.
    /// Returnerar <see cref="CommentStyle.CStyle"/> som fallback.
    /// </summary>
    public static CommentStyle DetectStyle(string fileExtension)
    {
        var ext = fileExtension.TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "CS" or "JAVA" or "JS" or "JSX" or "TS" or "TSX"
                or "CPP" or "C" or "H" or "GO" or "RS" or "KT" or "SWIFT"
                    => CommentStyle.CStyle,

            "PY" or "PYW" or "RB" or "RUBY" or "SH" or "BASH"
                or "ZSH" or "YML" or "YAML" or "R"
                    => CommentStyle.HashStyle,

            "SQL"   => CommentStyle.SqlStyle,
            "HTML" or "XML" or "SVG" or "XAML" or "RAZOR" or "CSHTML"
                    => CommentStyle.HtmlStyle,
            "CSS" or "SCSS" or "LESS"
                    => CommentStyle.CStyle,   // CSS använder /* */
            _       => CommentStyle.CStyle
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privata hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private static void StripComments(
        StringBuilder sb, CommentStyle style, NormalizationOptions opts)
    {
        var s = sb.ToString();

        switch (style)
        {
            case CommentStyle.CStyle:
                if (opts.StripDocComments)   s = DocCommentRx.Replace(s,   " ");
                if (opts.StripBlockComments)  s = BlockCommentRx.Replace(s,  " ");
                if (opts.StripLineComments)   s = LineCommentCStyleRx.Replace(s, " ");
                break;

            case CommentStyle.HashStyle:
                if (opts.StripLineComments)   s = LineCommentHashRx.Replace(s, " ");
                // Python docstrings hanteras av StripTripleQuoteStrings
                break;

            case CommentStyle.SqlStyle:
                if (opts.StripBlockComments)  s = BlockCommentRx.Replace(s, " ");
                if (opts.StripLineComments)   s = LineCommentSqlRx.Replace(s, " ");
                break;

            case CommentStyle.HtmlStyle:
                s = HtmlCommentRx.Replace(s, " ");
                // HTML-filer kan ha inline JS (<script>) — strip C-style kommentarer också
                if (opts.StripBlockComments)  s = BlockCommentRx.Replace(s, " ");
                if (opts.StripLineComments)   s = LineCommentCStyleRx.Replace(s, " ");
                break;

            case CommentStyle.None:
            default:
                return;
        }

        sb.Clear();
        sb.Append(s);
    }

    private static void StripTripleQuotes(StringBuilder sb)
    {
        var s = sb.ToString();
        s = TripleDoubleRx.Replace(s, " ");
        s = TripleSingleRx.Replace(s, " ");
        sb.Clear();
        sb.Append(s);
    }

    private static void CollapseWhitespace(StringBuilder sb)
    {
        var s = WhitespaceRx.Replace(sb.ToString(), " ");
        sb.Clear();
        sb.Append(s);
    }
}
