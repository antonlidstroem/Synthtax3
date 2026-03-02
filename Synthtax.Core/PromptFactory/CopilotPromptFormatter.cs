using System.Text;
using Synthtax.Core.Contracts;
using Synthtax.Core.Enums;

namespace Synthtax.Core.PromptFactory;

/// <summary>
/// Genererar korta, handlingsinriktade prompts optimerade för
/// GitHub Copilot Chat / Copilot Edits i VS Code / Visual Studio.
///
/// <para><b>Designprinciper:</b>
/// <list type="bullet">
///   <item>Max ~300 tokens — Copilot fungerar bäst med täta, precisa instruktioner.</item>
///   <item>Börja alltid med ett imperativt verb: "Fix", "Extract", "Replace", "Implement".</item>
///   <item>Peka direkt på filen och raden — Copilot kan hoppa dit.</item>
///   <item>Visa "vad" (det trasiga) och "varför" (regeln) — inte långa förklaringar.</item>
///   <item>Avsluta med önskat resultat i ett meningsfullt kodfragment.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class CopilotPromptFormatter
{
    public PromptResult Format(PromptRequest request)
    {
        var issue = request.Issue;
        var sb    = new StringBuilder();

        // ── Rubrik: imperativt + plats ────────────────────────────────────
        sb.AppendLine(BuildTitle(issue));
        sb.AppendLine();

        // ── Problemet i en rad ────────────────────────────────────────────
        sb.AppendLine($"**Issue [{issue.RuleId}]:** {issue.Message}");
        sb.AppendLine();

        // ── Problemkoden ──────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(issue.Snippet))
        {
            sb.AppendLine("**Nuvarande kod:**");
            sb.AppendLine(PromptBuilder.CodeBlock(issue.Snippet));
            sb.AppendLine();
        }

        // ── Åtgärd ────────────────────────────────────────────────────────
        sb.AppendLine("**Åtgärd:**");
        var action = BuildActionLine(issue);
        sb.AppendLine(action);

        // ── Fix-kod om autofix finns ──────────────────────────────────────
        if (issue.IsAutoFixable && !string.IsNullOrWhiteSpace(issue.FixedSnippet))
        {
            sb.AppendLine();
            sb.AppendLine("**Ersätt med:**");
            sb.AppendLine(PromptBuilder.CodeBlock(issue.FixedSnippet));
        }

        // ── Krav-rad ──────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.CodingConventions))
        {
            sb.AppendLine();
            sb.AppendLine($"*Konventioner: {request.CodingConventions}*");
        }

        return new PromptResult
        {
            PromptText        = sb.ToString().TrimEnd(),
            Target            = PromptTarget.Copilot,
            RuleId            = issue.RuleId,
            SuggestedCommands = BuildCommands(issue)
        };
    }

    // ── Privata byggare ───────────────────────────────────────────────────

    private static string BuildTitle(RawIssue issue)
    {
        var verb = issue.RuleId switch
        {
            var id when id.StartsWith("CA006") => "Implement",  // NotImplementedException
            var id when id.StartsWith("CA007") => "Extract",    // Type Extraction
            var id when id.StartsWith("CA008") => "Refactor",   // Method Extraction
            _ => issue.IsAutoFixable ? "Fix" : "Refactor"
        };

        var location = $"`{issue.FilePath}:{issue.StartLine}`";
        return $"**{verb}** {location} — {PromptBuilder.BuildScopePath(issue)}";
    }

    private static string BuildActionLine(RawIssue issue)
    {
        if (!string.IsNullOrWhiteSpace(issue.Suggestion))
            return issue.Suggestion;

        return issue.RuleId switch
        {
            var id when id.StartsWith("CA006") =>
                "Replace `throw new NotImplementedException()` with a working implementation. " +
                "Use the method signature and XML docs to infer the expected behavior.",

            var id when id.StartsWith("CA007") =>
                $"Move the secondary types in this file to separate files. " +
                $"Keep only `{issue.Scope.ClassName}` in {System.IO.Path.GetFileName(issue.FilePath)}.",

            var id when id.StartsWith("CA008") =>
                $"Split `{issue.Scope.MemberName}` into smaller focused methods. " +
                $"Each method should have a single responsibility.",

            _ => $"Fix the {issue.Category.ToLowerInvariant()} issue at line {issue.StartLine}."
        };
    }

    private static IReadOnlyList<string> BuildCommands(RawIssue issue) =>
        issue.RuleId switch
        {
            var id when id.StartsWith("CA006") => ["/fix", "/explain"],
            var id when id.StartsWith("CA007") => ["/fix"],
            var id when id.StartsWith("CA008") => ["/fix", "@workspace"],
            _ => ["/fix"]
        };
}
