using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Workspace;

/// <summary>
/// Encapsulates a fully-loaded Roslyn solution with all SyntaxRoots and SemanticModels
/// pre-built and cached. Build once, share across all analysis passes in a single run
/// to avoid redundant IO and compilation work.
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
        Solution  = solution;
        _workspace = workspace;
        Documents  = documents;
        _roots     = roots;
        _models    = models;
    }

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

        var roots  = new ConcurrentDictionary<DocumentId, SyntaxNode>();
        var models = new ConcurrentDictionary<DocumentId, SemanticModel?>();

        var parallelOpts = new ParallelOptions
        {
            CancellationToken       = ct,
            MaxDegreeOfParallelism  = options?.MaxDegreeOfParallelism > 0
                                        ? options.MaxDegreeOfParallelism
                                        : Environment.ProcessorCount
        };

        await Parallel.ForEachAsync(docs, parallelOpts, async (doc, token) =>
        {
            var root  = await doc.GetSyntaxRootAsync(token).ConfigureAwait(false);
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

    public SyntaxNode?    GetRoot(Document doc) => _roots.TryGetValue(doc.Id,  out var r) ? r : null;
    public SemanticModel? GetModel(Document doc) => _models.TryGetValue(doc.Id, out var m) ? m : null;
    public string         GetFilePath(Document doc) => doc.FilePath ?? doc.Name;

    public ValueTask DisposeAsync()
    {
        _workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Options controlling which modules the pipeline runs and at what parallelism.</summary>
public sealed class FullAnalysisOptions
{
    public bool IncludeCode        { get; set; } = true;
    public bool IncludeSecurity    { get; set; } = true;
    public bool IncludeMetrics     { get; set; } = true;
    public bool IncludeCoupling    { get; set; } = true;
    public bool IncludeAIDetection { get; set; } = false;
    public int  MaxDegreeOfParallelism { get; set; } = 0; // 0 = auto
}
