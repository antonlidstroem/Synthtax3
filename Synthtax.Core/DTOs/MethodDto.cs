namespace Synthtax.Core.DTOs;

public class MethodDto
{
    public string MethodName { get; set; } = string.Empty;
    public string FullSignature { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string NamespaceName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int LinesOfCode { get; set; }
    public int CyclomaticComplexity { get; set; }
    public string ReturnType { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
    public List<string> Modifiers { get; set; } = new();
    public bool IsAsync { get; set; }
    public bool IsStatic { get; set; }
    public bool IsPublic { get; set; }
    public string? XmlDocSummary { get; set; }
}

public class MethodExplorerResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public int TotalMethods { get; set; }
    public List<MethodDto> Methods { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
