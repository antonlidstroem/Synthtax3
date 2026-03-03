namespace Synthtax.Vsix.Client;

public sealed class BacklogItemDto
{
    public Guid    Id            { get; set; }
    public string  RuleId        { get; set; } = "";
    public string  Severity      { get; set; } = "Medium";
    public string  Status        { get; set; } = "Open";
    public string  FilePath      { get; set; } = "";
    public int     StartLine     { get; set; }
    public string  Message       { get; set; } = "";
    public string? ClassName     { get; set; }
    public string? MemberName    { get; set; }
    public string? Namespace     { get; set; }
    public bool    IsAutoFixable { get; set; }
    public string  Snippet       { get; set; } = "";
    public string? FixedSnippet  { get; set; }
    public string? Suggestion    { get; set; }
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
}
