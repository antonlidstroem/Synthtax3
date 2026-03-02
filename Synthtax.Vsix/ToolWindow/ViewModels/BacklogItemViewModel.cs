using CommunityToolkit.Mvvm.ComponentModel;
using Synthtax.Vsix.Client;

namespace Synthtax.Vsix.ToolWindow.ViewModels;

/// <summary>
/// Presentation-model för ett enskilt BacklogItem i Tool Window-listan.
/// Immutabel efter konstruktion — uppdatering sker via listbyte.
/// </summary>
public sealed class BacklogItemViewModel : ObservableObject
{
    public Guid   Id           { get; }
    public string RuleId       { get; }
    public string Severity     { get; }
    public string Status       { get; }
    public string FilePath     { get; }
    public int    StartLine    { get; }
    public string Message      { get; }
    public string? ClassName   { get; }
    public string? MemberName  { get; }
    public string? Suggestion  { get; }
    public string? FixedSnippet { get; }
    public bool   IsAutoFixable { get; }

    // ── Beräknade display-properties ──────────────────────────────────────
    public string FileName   => System.IO.Path.GetFileName(FilePath);
    public string Scope      => $"{ClassName ?? "?"}.{MemberName ?? "?"}";
    public string SeverityLevel => Severity; // alias för XAML-sortering

    // Underliggande DTO (behövs av CodeFix-kommandon)
    internal BacklogItemDto Dto { get; }

    public BacklogItemViewModel(BacklogItemDto dto)
    {
        Dto          = dto;
        Id           = dto.Id;
        RuleId       = dto.RuleId;
        Severity     = dto.Severity;
        Status       = dto.Status;
        FilePath     = dto.FilePath;
        StartLine    = dto.StartLine;
        Message      = dto.Message;
        ClassName    = dto.ClassName;
        MemberName   = dto.MemberName;
        Suggestion   = dto.Suggestion;
        FixedSnippet = dto.FixedSnippet;
        IsAutoFixable = dto.IsAutoFixable;
    }
}
