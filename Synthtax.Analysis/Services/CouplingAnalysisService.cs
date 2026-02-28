using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Synthtax.Analysis.Pipeline;
using Synthtax.Analysis.Workspace;
using Synthtax.Core.DTOs;
using Synthtax.Core.Interfaces;

namespace Synthtax.Analysis.Services;

public class CouplingAnalysisService : ICouplingAnalysisService, IContextAwareAnalysis
{
    private readonly ILogger<CouplingAnalysisService> _logger;
    private readonly IRoslynWorkspaceService _workspace;

    public CouplingAnalysisService(ILogger<CouplingAnalysisService> logger, IRoslynWorkspaceService workspace)
    {
        _logger    = logger;
        _workspace = workspace;
    }

    public async Task<object> AnalyzeAsync(AnalysisContext ctx, CancellationToken ct)
        => await RunOnContext(ctx, ctx.Solution.FilePath ?? "solution", ct);

    public async Task<CouplingAnalysisResultDto> AnalyzeSolutionAsync(
        string solutionPath, CancellationToken cancellationToken = default)
    {
        var (ws, sol) = await _workspace.LoadSolutionAsync(solutionPath, cancellationToken);
        await using var ctx = await AnalysisContext.BuildAsync(sol, ws, _workspace, null, _logger, cancellationToken);
        return await RunOnContext(ctx, solutionPath, cancellationToken);
    }

    private async Task<CouplingAnalysisResultDto> RunOnContext(
        AnalysisContext ctx, string solutionPath, CancellationToken ct)
    {
        var result = new CouplingAnalysisResultDto { SolutionPath = solutionPath };
        try
        {
            var typeData = new ConcurrentDictionary<string, TypeData>();

            // Pass 1 – collect all types
            await Parallel.ForEachAsync(ctx.Documents, new ParallelOptions { CancellationToken = ct },
                (doc, token) =>
                {
                    var root  = ctx.GetRoot(doc);
                    var model = ctx.GetModel(doc);
                    if (root is null || model is null) return ValueTask.CompletedTask;
                    var filePath = ctx.GetFilePath(doc);

                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (typeDecl.Parent is TypeDeclarationSyntax) continue;
                        if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol sym) continue;
                        var fqn = sym.ToDisplayString();
                        typeData.GetOrAdd(fqn, _ => new TypeData
                        {
                            Symbol    = sym,
                            FilePath  = filePath,
                            Namespace = sym.ContainingNamespace?.ToDisplayString() ?? "Global"
                        });
                    }
                    return ValueTask.CompletedTask;
                });

            var typeNames = typeData.Keys.ToHashSet(StringComparer.Ordinal);

            // Pass 2 – compute coupling
            await Parallel.ForEachAsync(ctx.Documents, new ParallelOptions { CancellationToken = ct },
                (doc, token) =>
                {
                    var root  = ctx.GetRoot(doc);
                    var model = ctx.GetModel(doc);
                    if (root is null || model is null) return ValueTask.CompletedTask;

                    foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (typeDecl.Parent is TypeDeclarationSyntax) continue;
                        if (model.GetDeclaredSymbol(typeDecl) is not INamedTypeSymbol ownerSym) continue;
                        var ownerFqn = ownerSym.ToDisplayString();
                        if (!typeData.TryGetValue(ownerFqn, out var owner)) continue;

                        foreach (var id in typeDecl.DescendantNodes().OfType<IdentifierNameSyntax>())
                        {
                            var refSym = model.GetSymbolInfo(id).Symbol;
                            INamedTypeSymbol? refType = refSym switch
                            {
                                INamedTypeSymbol t  => t,
                                IMethodSymbol m     => m.ContainingType,
                                IPropertySymbol p   => p.ContainingType,
                                IFieldSymbol f      => f.ContainingType,
                                _                   => null
                            };
                            if (refType is null) continue;
                            var refFqn = refType.ToDisplayString();
                            if (refFqn == ownerFqn || !typeNames.Contains(refFqn)) continue;

                            lock (owner) owner.Efferents.Add(refFqn);
                            if (typeData.TryGetValue(refFqn, out var dep))
                                lock (dep) dep.Afferents.Add(ownerFqn);
                        }
                    }
                    return ValueTask.CompletedTask;
                });

            foreach (var (fqn, data) in typeData)
            {
                var sym = data.Symbol;
                var ca  = data.Afferents.Count;
                var ce  = data.Efferents.Count;
                var i   = ca + ce > 0 ? (double)ce / (ca + ce) : 0;
                var a   = ComputeAbstractness(sym);
                var d   = Math.Abs(a + i - 1);

                result.Types.Add(new TypeCouplingDto
                {
                    TypeName                    = sym.Name,
                    Namespace                   = data.Namespace,
                    FilePath                    = data.FilePath,
                    AfferentCoupling            = ca,
                    EfferentCoupling            = ce,
                    Instability                 = Math.Round(i, 3),
                    Abstractness                = Math.Round(a, 3),
                    DistanceFromMainSequence    = Math.Round(d, 3),
                    DependsOn                   = data.Efferents.ToList(),
                    DependedOnBy                = data.Afferents.ToList(),
                    Verdict                     = ClassifyType(ca, ce, i, d, sym)
                });
            }

            result.Types.Sort((x, y) => y.DistanceFromMainSequence.CompareTo(x.DistanceFromMainSequence));

            result.Namespaces.AddRange(
                result.Types
                    .GroupBy(t => t.Namespace)
                    .Select(g => new NamespaceCouplingDto
                    {
                        Namespace             = g.Key,
                        TypeCount             = g.Count(),
                        Abstractness          = Math.Round(g.Average(t => t.Abstractness), 3),
                        Instability           = Math.Round(g.Average(t => t.Instability), 3),
                        Distance              = Math.Round(g.Average(t => t.DistanceFromMainSequence), 3),
                        OutgoingDependencies  = g.SelectMany(t => t.DependsOn)
                            .Select(dep => typeData.TryGetValue(dep, out var td) ? td.Namespace : dep)
                            .Distinct().Where(ns => ns != g.Key).ToList()
                    })
                    .OrderByDescending(n => n.Distance));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coupling analysis error for {Path}", solutionPath);
            result.Errors.Add($"Coupling error: {ex.Message}");
        }
        return result;
    }

    private static double ComputeAbstractness(INamedTypeSymbol sym)
    {
        if (sym.TypeKind == TypeKind.Interface) return 1.0;
        var publicMembers = sym.GetMembers()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public
                     && m is not IMethodSymbol { MethodKind: MethodKind.Constructor })
            .ToList();
        if (publicMembers.Count == 0) return 0;
        var abstractCount = publicMembers.Count(m => m.IsAbstract || m.IsVirtual);
        return (double)abstractCount / publicMembers.Count;
    }

    private static CouplingVerdict ClassifyType(int ca, int ce, double i, double d, INamedTypeSymbol sym)
    {
        if (sym.TypeKind is TypeKind.Interface)    return CouplingVerdict.Healthy;
        if (ca + ce > 20 && ce > 10)               return CouplingVerdict.GodClass;
        if (ce > 10)                               return CouplingVerdict.TightlyCoupled;
        if (i > 0.8 && ce > 5)                    return CouplingVerdict.Unstable;
        return CouplingVerdict.Healthy;
    }

    private sealed class TypeData
    {
        public INamedTypeSymbol Symbol    { get; set; } = null!;
        public string FilePath            { get; set; } = string.Empty;
        public string Namespace           { get; set; } = string.Empty;
        public HashSet<string> Afferents  { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Efferents  { get; } = new(StringComparer.Ordinal);
    }
}
