using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;
using System.Text.RegularExpressions;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// Heuristisk AI-detektering baserad på kodmönster typiska för AI-genererad C#-kod.
/// OBS: Experimentell funktion – falska positiver förekommer.
/// </summary>
public class AIDetectionService : IAIDetectionService
{
    private readonly ILogger<AIDetectionService> _logger;

    // Phrases extremely common in AI-generated XML doc comments
    private static readonly string[] AiDocPhrases =
    {
        "Gets or sets", "Represents a", "Initializes a new instance",
        "Provides functionality", "Defines the", "Encapsulates",
        "A class that", "This class", "This method", "Gets the",
        "Returns the", "Determines whether"
    };

    // Over-engineered variable / method naming patterns AI tends to produce
    private static readonly Regex AiNamingPattern = new(
        @"\b(result|response|data|output|returnValue|finalResult|processedData|executionResult)\b",
        RegexOptions.Compiled);

    // AI tends to add redundant null checks like: if (x == null) throw new ArgumentNullException(nameof(x));
    private static readonly Regex NullGuardPattern = new(
        @"if\s*\(\s*\w+\s*==\s*null\s*\)\s*(throw|return)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // AI tends to comment every brace closing: // end if, // end for
    private static readonly Regex ClosingBraceCommentPattern = new(
        @"}\s*//\s*(end|close|endif|endfor|endwhile|endtry)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public AIDetectionService(ILogger<AIDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<AIDetectionResultDto> AnalyzeSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        var result = new AIDetectionResultDto { SolutionPath = solutionPath };

        try
        {
            var (workspace, solution) = await RoslynWorkspaceHelper.LoadSolutionAsync(
                solutionPath, _logger, cancellationToken);

            using (workspace)
            {
                foreach (var doc in RoslynWorkspaceHelper.GetCSharpDocuments(solution))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileResult = await AnalyzeDocumentAsync(doc, cancellationToken);
                    result.FileResults.Add(fileResult);
                }
            }

            result.FilesAnalyzed = result.FileResults.Count;
            result.FilesWithHighScore = result.FileResults.Count(f => f.AILikelihoodScore >= 0.6);
            result.OverallScore = result.FileResults.Count > 0
                ? Math.Round(result.FileResults.Average(f => f.AILikelihoodScore), 3)
                : 0;
            result.OverallVerdict = ScoreToVerdict(result.OverallScore);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI detection error for {Path}", solutionPath);
            result.Errors.Add($"AI detection error: {ex.Message}");
        }

        return result;
    }

    public async Task<AIDetectionFileResultDto> AnalyzeFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var code = await File.ReadAllTextAsync(filePath, cancellationToken);
        return await AnalyzeCodeTextAsync(code, Path.GetFileName(filePath), cancellationToken);
    }

    public async Task<AIDetectionFileResultDto> AnalyzeCodeTextAsync(
        string code,
        string virtualFileName = "input.cs",
        CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(code, path: virtualFileName,
            cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        return ScoreFile(root, code, virtualFileName, virtualFileName);
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private static async Task<AIDetectionFileResultDto> AnalyzeDocumentAsync(
        Document doc, CancellationToken cancellationToken)
    {
        var root = await doc.GetSyntaxRootAsync(cancellationToken);
        var text = root?.ToFullString() ?? string.Empty;
        var filePath = doc.FilePath ?? doc.Name;
        return ScoreFile(root!, text, filePath, doc.Name);
    }

    private static AIDetectionFileResultDto ScoreFile(
        SyntaxNode root, string code, string filePath, string fileName)
    {
        var signals = new List<AIDetectionSignalDto>();
        var lines = code.Split('\n');
        var totalLines = lines.Length;

        if (totalLines < 5)
            return new AIDetectionFileResultDto
            {
                FilePath = filePath, FileName = fileName,
                AILikelihoodScore = 0, Verdict = "Unlikely"
            };

        // ── Signal 1: XML doc comment saturation ──────────────────────────
        var xmlDocLines = lines.Count(l => l.TrimStart().StartsWith("///"));
        var docRatio = (double)xmlDocLines / totalLines;
        if (docRatio > 0.25)
        {
            signals.Add(new AIDetectionSignalDto
            {
                SignalType = "HighXmlDocDensity",
                Description = $"XML doc comment density is {docRatio:P0} — AI tools often over-document.",
                Weight = Math.Min(0.3, docRatio),
                FilePath = filePath
            });
        }

        // ── Signal 2: AI doc phrases ──────────────────────────────────────
        var docPhrasesFound = AiDocPhrases.Count(p =>
            code.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (docPhrasesFound >= 3)
        {
            signals.Add(new AIDetectionSignalDto
            {
                SignalType = "AiDocPhrases",
                Description = $"Found {docPhrasesFound} common AI-generated comment phrases.",
                Weight = Math.Min(0.25, docPhrasesFound * 0.05),
                FilePath = filePath,
                Evidence = string.Join(", ", AiDocPhrases
                    .Where(p => code.Contains(p, StringComparison.OrdinalIgnoreCase))
                    .Take(5))
            });
        }

        // ── Signal 3: Generic variable naming ────────────────────────────
        var namingMatches = AiNamingPattern.Matches(code).Count;
        var methodCount = root?.DescendantNodes().OfType<MethodDeclarationSyntax>().Count() ?? 1;
        var namingRatio = methodCount > 0 ? (double)namingMatches / methodCount : 0;
        if (namingRatio > 1.5)
        {
            signals.Add(new AIDetectionSignalDto
            {
                SignalType = "GenericVariableNames",
                Description = $"High use of generic variable names (result, response, data) — {namingMatches} occurrences across {methodCount} methods.",
                Weight = Math.Min(0.2, namingRatio * 0.05),
                FilePath = filePath
            });
        }

        // ── Signal 4: Excessive null guard pattern ────────────────────────
        var nullGuards = NullGuardPattern.Matches(code).Count;
        if (methodCount > 0 && (double)nullGuards / methodCount > 2.0)
        {
            signals.Add(new AIDetectionSignalDto
            {
                SignalType = "ExcessiveNullGuards",
                Description = $"Unusually high density of null-guard patterns ({nullGuards} for {methodCount} methods).",
                Weight = 0.1,
                FilePath = filePath
            });
        }

        // ── Signal 5: Closing brace comments ─────────────────────────────
        var closingComments = ClosingBraceCommentPattern.Matches(code).Count;
        if (closingComments > 0)
        {
            signals.Add(new AIDetectionSignalDto
            {
                SignalType = "ClosingBraceComments",
                Description = $"Found {closingComments} closing-brace comments (// end if, // end for). Rarely written by humans.",
                Weight = Math.Min(0.25, closingComments * 0.08),
                FilePath = filePath
            });
        }

        // ── Signal 6: Every public member has XML doc ─────────────────────
        if (root is not null)
        {
            var publicMembers = root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)))
                .ToList();

            if (publicMembers.Count >= 3)
            {
                var membersWithDoc = publicMembers.Count(m =>
                    m.GetLeadingTrivia().Any(t =>
                        t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                        t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)));

                var docCoverage = (double)membersWithDoc / publicMembers.Count;
                if (docCoverage >= 0.9)
                {
                    signals.Add(new AIDetectionSignalDto
                    {
                        SignalType = "FullDocCoverage",
                        Description = $"{docCoverage:P0} of public members are XML-documented. Human code rarely achieves this.",
                        Weight = Math.Min(0.2, (docCoverage - 0.5) * 0.4),
                        FilePath = filePath
                    });
                }
            }
        }

        // ── Signal 7: Consistent formatting uniformity ────────────────────
        var indentLengths = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Length - l.TrimStart().Length)
            .Where(n => n > 0)
            .ToList();

        if (indentLengths.Count > 10)
        {
            var allDivisibleBy4 = indentLengths.All(n => n % 4 == 0);
            if (allDivisibleBy4)
            {
                signals.Add(new AIDetectionSignalDto
                {
                    SignalType = "PerfectIndentation",
                    Description = "100% consistent 4-space indentation throughout file. Unusual for large human-written files.",
                    Weight = 0.05,
                    FilePath = filePath
                });
            }
        }

        // ── Compute final score ───────────────────────────────────────────
        var rawScore = signals.Sum(s => s.Weight);
        var finalScore = Math.Clamp(rawScore, 0.0, 1.0);

        return new AIDetectionFileResultDto
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            AILikelihoodScore = Math.Round(finalScore, 3),
            Verdict = ScoreToVerdict(finalScore),
            Signals = signals
        };
    }

    private static string ScoreToVerdict(double score) => score switch
    {
        < 0.2 => "Unlikely",
        < 0.4 => "Possible",
        < 0.65 => "Probable",
        _ => "Likely"
    };

    public Task<AIDetectionFileResultDto> AnalyzeCodeAsync(string code, string fileName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
