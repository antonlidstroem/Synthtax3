// BUG-04 FIX: Filen låg tidigare felaktigt i Synthtax.Analysis/ med namespace
// Synthtax.Core.DTOs. Den hör hemma i Synthtax.Core/DTOs/ fysiskt och logiskt.
//
// Ta bort: Synthtax.Analysis/SemanticAnalysisResultDto.cs
// Lägg till: Synthtax.Core/DTOs/SemanticAnalysisResultDto.cs  (denna fil)

namespace Synthtax.Core.DTOs;

public class SemanticAnalysisResultDto
{
    public string SolutionPath { get; set; } = string.Empty;
    public string AnalysisType { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public int TotalIssues { get; set; }
    public List<SavedIssueDto> Issues { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
