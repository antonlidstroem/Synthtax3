using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;
using Synthtax.Core.Entities;

namespace Synthtax.Core.PromptFactory;

public enum PromptTarget
{
    Copilot = 0,
    Claude  = 1,
    General = 2
}

public sealed record PromptRequest
{
    public required PromptTarget              Target              { get; init; }
    public required RawIssue                  Issue               { get; init; }
    public          string?                   ProjectTechStack    { get; init; }
    public          string?                   ArchitecturePattern { get; init; }
    public          IReadOnlyList<RelatedFile>? RelatedFiles      { get; init; }
    public          BacklogItem?              ExistingBacklogItem { get; init; }
    public          string?                   CodingConventions   { get; init; }
}

public sealed record RelatedFile(
    string  FilePath,
    string  Content,
    string? Description = null);

public sealed record PromptResult
{
    public required string               PromptText        { get; init; }
    public          int                  EstimatedTokens   => PromptText.Length / 4;
    public required PromptTarget         Target            { get; init; }
    public required string               RuleId            { get; init; }
    public          DateTime             GeneratedAt       { get; init; } = DateTime.UtcNow;
    public          IReadOnlyList<string> SuggestedCommands { get; init; } = [];
}

public interface IPromptFactoryService
{
    PromptResult Generate(PromptRequest request);

    IReadOnlyList<PromptResult> GenerateBatch(
        IReadOnlyList<RawIssue> issues,
        PromptTarget             target,
        string?                  projectTechStack = null);
}
