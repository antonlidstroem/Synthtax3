namespace Synthtax.Core.DTOs;

public class StructureNodeDto
{
    public string Name { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty; // Solution, Project, Namespace, Class, Interface, Enum, Struct, Method, Property, Field
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public string? Modifier { get; set; }
    public string? ReturnType { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsStatic { get; set; }
    public List<StructureNodeDto> Children { get; set; } = new();
}

public class StructureAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public StructureNodeDto? RootNode { get; set; }
    public List<string> Errors { get; set; } = new();
}
