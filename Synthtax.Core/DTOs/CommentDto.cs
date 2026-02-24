namespace Synthtax.Core.DTOs;

public class CommentDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string CommentType { get; set; } = string.Empty; // SingleLine, MultiLine, XmlDoc, InlineDoc
    public string Content { get; set; } = string.Empty;
    public string? AssociatedMember { get; set; }
}

public class RegionDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string RegionName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int LinesOfCode { get; set; }
}

public class CommentExplorerResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public int TotalComments { get; set; }
    public int TotalRegions { get; set; }
    public int XmlDocComments { get; set; }
    public int TodoComments { get; set; }
    public List<CommentDto> Comments { get; set; } = new();
    public List<RegionDto> Regions { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
