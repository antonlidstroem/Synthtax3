namespace Synthtax.Core.DTOs;

public class TypeCouplingDto
{
    public string TypeName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Afferent coupling — how many types depend ON this type.</summary>
    public int AfferentCoupling { get; set; }

    /// <summary>Efferent coupling — how many types this type depends on.</summary>
    public int EfferentCoupling { get; set; }

    /// <summary>I = Ce / (Ca + Ce).  0 = maximally stable, 1 = maximally unstable.</summary>
    public double Instability { get; set; }

    /// <summary>A = abstract or interface members / total public members.</summary>
    public double Abstractness { get; set; }

    /// <summary>D = |A + I - 1|.  Distance from the "main sequence" line.</summary>
    public double DistanceFromMainSequence { get; set; }

    public List<string> DependsOn { get; set; } = new();
    public List<string> DependedOnBy { get; set; } = new();
    public CouplingVerdict Verdict { get; set; }
}

public enum CouplingVerdict
{
    Healthy = 0,
    Unstable = 1,
    TightlyCoupled = 2,
    GodClass = 3
}

public class NamespaceCouplingDto
{
    public string Namespace { get; set; } = string.Empty;
    public int TypeCount { get; set; }
    public double Abstractness { get; set; }
    public double Instability { get; set; }
    public double Distance { get; set; }
    public List<string> OutgoingDependencies { get; set; } = new();
}

public class CouplingAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public List<TypeCouplingDto> Types { get; set; } = new();
    public List<NamespaceCouplingDto> Namespaces { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public int TightlyCoupledCount => Types.Count(t => t.Verdict == CouplingVerdict.TightlyCoupled);
    public int GodClassCount => Types.Count(t => t.Verdict == CouplingVerdict.GodClass);
    public double AverageInstability => Types.Count > 0 ? Types.Average(t => t.Instability) : 0;
}
