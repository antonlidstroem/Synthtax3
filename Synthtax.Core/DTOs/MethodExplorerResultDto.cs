namespace Synthtax.Core.DTOs;

public class MethodExplorerResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public List<MethodDto> Methods { get; set; } = new();
    public int TotalMethods { get; set; }
    public List<string> Errors { get; set; } = new();
}
