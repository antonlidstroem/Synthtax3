namespace Synthtax.Core.DTOs;

public class RefactoringSuggestionDto
{
    public string RefactoringType { get; set; } = string.Empty;  // e.g. "ExtractMethod"
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string OriginalCode { get; set; } = string.Empty;
    public string SuggestedCode { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public RefactoringImpact Impact { get; set; }
    public int EstimatedComplexityReduction { get; set; }
}

public enum RefactoringImpact { Low, Medium, High }

public class RefactoringResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public List<RefactoringSuggestionDto> Suggestions { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public int TotalSuggestions => Suggestions.Count;
    public int HighImpactCount => Suggestions.Count(s => s.Impact == RefactoringImpact.High);
}
