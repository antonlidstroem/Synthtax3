using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Interfaces;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// A "session" object that loads the solution once and caches every
/// SyntaxRoot + SemanticModel.
///
/// Problems solved:
///   • Each service previously called MSBuildWorkspace.OpenSolutionAsync()
///     independently — O(N) expensive loads for N services.
///   • SemanticModel construction was repeated per service.
///   • Sequential await per document meant no parallelism.
///
/// This context loads all roots+models in parallel (Parallel.ForEachAsync)
/// then exposes them as a cheap synchronous lookup.
/// </summary>
public sealed class AnalysisContext : IAsyncDisposable
{
    private readonly MSBuildWorkspace _workspace;
    private readonly ImmutableDictionary<DocumentId, SyntaxNode> _roots;
    private readonly ImmutableDictionary<DocumentId, SemanticModel?> _models;

    public Solution Solution { get; }
    public IReadOnlyList<Document> Documents { get; }

    private AnalysisContext(
        Solution solution,
        MSBuildWorkspace workspace,
        IReadOnlyList<Document> documents,
        ImmutableDictionary<DocumentId, SyntaxNode> roots,
        ImmutableDictionary<DocumentId, SemanticModel?> models)
    {
        Solution = solution;
        _workspace = workspace;
        Documents = documents;
        _roots = roots;
        _models = models;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    public static async Task<AnalysisContext> BuildAsync(
        Solution solution,
        MSBuildWorkspace workspace,
        IRoslynWorkspaceService workspaceService,
        FullAnalysisOptions? options,
        ILogger logger,
        CancellationToken ct = default)
    {
        var docs = workspaceService.GetCSharpDocuments(solution).ToList();
        logger.LogInformation("AnalysisContext: building for {Count} document(s).", docs.Count);

        var roots = new ConcurrentDictionary<DocumentId, SyntaxNode>();
        var models = new ConcurrentDictionary<DocumentId, SemanticModel?>();

        var parallelOpts = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = options?.MaxDegreeOfParallelism > 0
                                       ? options.MaxDegreeOfParallelism
                                       : Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(docs, parallelOpts, async (doc, token) =>
        {
            var root = await doc.GetSyntaxRootAsync(token).ConfigureAwait(false);
            if (root is not null) roots[doc.Id] = root;

            var model = await doc.GetSemanticModelAsync(token).ConfigureAwait(false);
            models[doc.Id] = model;
        });

        logger.LogInformation(
            "AnalysisContext ready: {Roots} roots, {Models} models.",
            roots.Count, models.Count);

        return new AnalysisContext(
            solution, workspace, docs,
            roots.ToImmutableDictionary(),
            models.ToImmutableDictionary());
    }

    // ── Accessors ─────────────────────────────────────────────────────────────

    public SyntaxNode? GetRoot(Document doc) =>
        _roots.TryGetValue(doc.Id, out var r) ? r : null;

    public SemanticModel? GetModel(Document doc) =>
        _models.TryGetValue(doc.Id, out var m) ? m : null;

    public string GetFilePath(Document doc) => doc.FilePath ?? doc.Name;

    // ── Disposal ─────────────────────────────────────────────────────────────

    public ValueTask DisposeAsync()
    {
        _workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}
