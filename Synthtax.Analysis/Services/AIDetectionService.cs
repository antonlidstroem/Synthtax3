using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Pipeline;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class AIDetectionService : IAIDetectionService, IContextAwareAnalysis
{
    private readonly ILogger<AIDetectionService> _logger;
    private readonly IRoslynWorkspaceService _workspace;

    public AIDetectionService(ILogger<AIDetectionService> logger, IRoslynWorkspaceService workspace)
    {
        _logger    = logger;
        _workspace = workspace;
    }

    private static readonly string[] AiDocPhrases =
    {
        "Gets or sets", "Represents a", "Initializes a new instance",
        "Provides functionality", "Defines the", "Encapsulates",
        "A class that", "This class", "This method", "Gets the",
        "Returns the", "Determines whether"
    };

    private static readonly Regex AiNamingPattern = new(
        @"\b(result|response|data|output|returnValue|finalResult|processedData|executionResult)\b",
        RegexOptions.Compiled);

    private static readonly Regex ClosingBraceCommentPattern = new(
        @"}\s*//\s*(end|close|endif|endfor|endwhile|endtry)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CamelCase = new(@"^[a-z][a-zA-Z0-9]*$", RegexOptions.Compiled);
    private static readonly Regex UnderScore = new(@"^[a-z]+(_[a-z0-9]+)+$", RegexOptions.Compiled);

    public async Task<object> AnalyzeAsync(AnalysisContext ctx, CancellationToken ct)
        => await RunOnContext(ctx, ctx.Solution.FilePath ?? "solution", ct);

    public async Task<AIDetectionResultDto> AnalyzeSolutionAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (ws, sol) = await _workspace.LoadSolutionAsync(solutionPath, cancellationToken);
        await using var ctx = await AnalysisContext.BuildAsync(sol, ws, _workspace, null, _logger, cancellationToken);
        return await RunOnContext(ctx, solutionPath, cancellationToken);
    }

    public async Task<AIDetectionFileResultDto> AnalyzeFileAsync(
        string filePath, CancellationToken cancellationToken = default)
    {
        var code = await File.ReadAllTextAsync(filePath, cancellationToken);
        return await AnalyzeCodeTextAsync(code, Path.GetFileName(filePath), cancellationToken);
    }

    public async Task<AIDetectionFileResultDto> AnalyzeCodeTextAsync(
        string code, string virtualFileName = "input.cs", CancellationToken cancellationToken = default)
    {
        var tree = CSharpSyntaxTree.ParseText(code, path: virtualFileName, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken);
        return ScoreFile(root, null, code, virtualFileName, virtualFileName);
    }

    public Task<AIDetectionFileResultDto> AnalyzeCodeAsync(string code, string fileName, CancellationToken ct)
        => AnalyzeCodeTextAsync(code, fileName, ct);

    // ── private helpers ────────────────────────────────────────────────────────

    private async Task<AIDetectionResultDto> RunOnContext(
        AnalysisContext ctx, string solutionPath, CancellationToken ct)
    {
        var result  = new AIDetectionResultDto { SolutionPath = solutionPath };
        var fileBag = new ConcurrentBag<AIDetectionFileResultDto>();
        try
        {
            await Parallel.ForEachAsync(ctx.Documents,
                new ParallelOptions { CancellationToken = ct },
                (doc, token) =>
                {
                    var root  = ctx.GetRoot(doc);
                    var model = ctx.GetModel(doc);
                    if (root is null) return ValueTask.CompletedTask;
                    fileBag.Add(ScoreFile(root, model, root.ToFullString(), ctx.GetFilePath(doc), doc.Name));
                    return ValueTask.CompletedTask;
                });

            result.FileResults.AddRange(fileBag);
            result.FilesAnalyzed      = result.FileResults.Count;
            result.FilesWithHighScore = result.FileResults.Count(f => f.AILikelihoodScore >= 0.6);
            result.OverallScore       = result.FileResults.Count > 0
                ? Math.Round(result.FileResults.Average(f => f.AILikelihoodScore), 3) : 0;
            result.OverallVerdict     = ScoreToVerdict(result.OverallScore);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI detection error for {Path}", solutionPath);
            result.Errors.Add($"AI detection error: {ex.Message}");
        }
        return result;
    }

    private static AIDetectionFileResultDto ScoreFile(
        SyntaxNode root, SemanticModel? model, string code, string filePath, string fileName)
    {
        var signals = new List<AIDetectionSignalDto>();
        var lines   = code.Split('\n');
        var total   = lines.Length;

        if (total < 5)
            return new AIDetectionFileResultDto
            {
                FilePath = filePath, FileName = fileName,
                AILikelihoodScore = 0, Verdict = "Unlikely"
            };

        var xmlDocLines = lines.Count(l => l.TrimStart().StartsWith("///"));
        var docRatio    = (double)xmlDocLines / total;
        if (docRatio > 0.25)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "HighXmlDocDensity",
                Description = $"XML doc density {docRatio:P0} — AI tools over-document.",
                Weight      = Math.Min(0.3, docRatio), FilePath = filePath
            });

        var phraseCount = AiDocPhrases.Count(p => code.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (phraseCount >= 3)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "AiDocPhrases",
                Description = $"{phraseCount} common AI-generated comment phrases found.",
                Weight      = Math.Min(0.25, phraseCount * 0.05), FilePath = filePath,
                Evidence    = string.Join(", ", AiDocPhrases
                    .Where(p => code.Contains(p, StringComparison.OrdinalIgnoreCase)).Take(5))
            });

        var methodCount  = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
        var namingMatches = AiNamingPattern.Matches(code).Count;
        var namingRatio   = methodCount > 0 ? (double)namingMatches / methodCount : 0;
        if (namingRatio > 1.5)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "GenericVariableNames",
                Description = $"{namingMatches} generic variable names across {methodCount} methods.",
                Weight      = Math.Min(0.2, namingRatio * 0.05), FilePath = filePath
            });

        var closingComments = ClosingBraceCommentPattern.Matches(code).Count;
        if (closingComments > 0)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "ClosingBraceComments",
                Description = $"{closingComments} closing-brace comments. Rarely human-written.",
                Weight      = Math.Min(0.25, closingComments * 0.08), FilePath = filePath
            });

        var publicMembers = root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)))
            .ToList();

        if (publicMembers.Count >= 3)
        {
            var docCoverage = (double)publicMembers.Count(m =>
                m.GetLeadingTrivia().Any(t =>
                    t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                    t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia)))
                / publicMembers.Count;

            if (docCoverage >= 0.9)
                signals.Add(new AIDetectionSignalDto
                {
                    SignalType  = "FullDocCoverage",
                    Description = $"{docCoverage:P0} of public members documented. Human code rarely achieves this.",
                    Weight      = Math.Min(0.2, (docCoverage - 0.5) * 0.4), FilePath = filePath
                });
        }

        var indents = lines.Where(l => !string.IsNullOrWhiteSpace(l))
                           .Select(l => l.Length - l.TrimStart().Length)
                           .Where(n => n > 0).ToList();
        if (indents.Count > 10 && indents.All(n => n % 4 == 0))
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "PerfectIndentation",
                Description = "100% consistent 4-space indentation. Unusual for large human-written files.",
                Weight      = 0.05, FilePath = filePath
            });

        CheckVariableConsistency(root, filePath, signals);
        CheckBoilerplateRatio(root, filePath, signals);
        if (model is not null) CheckHallucinatedApis(root, model, filePath, signals);
        CheckNamingConsistency(root, filePath, signals);

        var finalScore = Math.Clamp(signals.Sum(s => s.Weight), 0.0, 1.0);
        return new AIDetectionFileResultDto
        {
            FilePath          = filePath,
            FileName          = Path.GetFileName(filePath),
            AILikelihoodScore = Math.Round(finalScore, 3),
            Verdict           = ScoreToVerdict(finalScore),
            Signals           = signals
        };
    }

    private static readonly string[][] ResultSynonyms =
    {
        new[] { "result", "response", "output", "returnValue", "ret" },
        new[] { "data", "payload", "content", "body" },
        new[] { "items", "list", "collection", "records", "entities" },
    };

    private static void CheckVariableConsistency(SyntaxNode root, string filePath, List<AIDetectionSignalDto> signals)
    {
        foreach (var synonymGroup in ResultSynonyms)
        {
            var found = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Select(v => v.Identifier.Text.ToLowerInvariant())
                .Where(n => synonymGroup.Contains(n)).Distinct().ToList();

            if (found.Count >= 3)
            {
                signals.Add(new AIDetectionSignalDto
                {
                    SignalType  = "VariableNameInconsistency",
                    Description = $"Multiple synonymous variable names: [{string.Join(", ", found)}].",
                    Weight      = 0.1, FilePath = filePath, Evidence = string.Join(", ", found)
                });
                break;
            }
        }
    }

    private static void CheckBoilerplateRatio(SyntaxNode root, string filePath, List<AIDetectionSignalDto> signals)
    {
        var allMembers = root.DescendantNodes().OfType<MemberDeclarationSyntax>().Count();
        if (allMembers == 0) return;
        var autoProps = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Count(p => p.AccessorList?.Accessors.All(a => a.Body is null && a.ExpressionBody is null) == true);
        var trivialCtors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
            .Count(c => (c.Body?.Statements.Count ?? 0) <= 3);
        var ratio = (double)(autoProps + trivialCtors) / allMembers;
        if (ratio > 0.60 && allMembers > 5)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "HighBoilerplateRatio",
                Description = $"{ratio:P0} of members are boilerplate. AI code often scaffolds excessively.",
                Weight      = Math.Min(0.15, (ratio - 0.5) * 0.3), FilePath = filePath
            });
    }

    private static void CheckHallucinatedApis(SyntaxNode root, SemanticModel model, string filePath, List<AIDetectionSignalDto> signals)
    {
        var count = 0;
        foreach (var ma in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            var sym = model.GetSymbolInfo(ma);
            if (sym.Symbol is not null || sym.CandidateSymbols.Length > 0) continue;
            var diags = model.GetDiagnostics(ma.Span);
            if (diags.Any(d => d.Id is "CS1061" or "CS0117" or "CS0122")) count++;
        }
        if (count > 0)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "PossibleHallucinatedApi",
                Description = $"{count} member access(es) reference non-existent members.",
                Weight      = Math.Min(0.3, count * 0.08), FilePath = filePath
            });
    }

    private static void CheckNamingConsistency(SyntaxNode root, string filePath, List<AIDetectionSignalDto> signals)
    {
        var localNames = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Select(v => v.Identifier.Text).Where(n => n.Length >= 3).ToList();
        if (localNames.Count < 5) return;
        int camel = localNames.Count(n => CamelCase.IsMatch(n));
        int under = localNames.Count(n => UnderScore.IsMatch(n));
        if (camel > 2 && under > 2)
            signals.Add(new AIDetectionSignalDto
            {
                SignalType  = "InconsistentNamingConvention",
                Description = $"Mixed naming: {camel} camelCase + {under} underscore_style variables.",
                Weight      = 0.08, FilePath = filePath
            });
    }

    private static string ScoreToVerdict(double score) => score switch
    {
        < 0.2  => "Unlikely",
        < 0.4  => "Possible",
        < 0.65 => "Probable",
        _      => "Likely"
    };
}
