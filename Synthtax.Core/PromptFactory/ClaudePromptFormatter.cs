using System.Text;
using Synthtax.Core.Contracts;
using Synthtax.Domain.Enums;

namespace Synthtax.Core.PromptFactory;

/// <summary>
/// Genererar fullständiga "Technical Spec"-prompts optimerade för Claude.
///
/// <para><b>Output-struktur:</b>
/// <code>
/// &lt;task&gt;Kortfattad uppgiftsbeskrivning&lt;/task&gt;
/// &lt;context&gt;
///   Teknisk stack, arkitekturmönster, issuets bakgrund
/// &lt;/context&gt;
/// &lt;problem_analysis&gt;
///   Detaljerad analys av vad som är fel och varför
/// &lt;/problem_analysis&gt;
/// &lt;code&gt;Problematisk kod&lt;/code&gt;
/// &lt;requirements&gt;
///   Konkreta krav som lösningen ska uppfylla
/// &lt;/requirements&gt;
/// &lt;constraints&gt;
///   Begränsningar: ramverk, konventioner, bakåtkompatibilitet
/// &lt;/constraints&gt;
/// &lt;related_files&gt;Relevant kodfiler&lt;/related_files&gt;
/// &lt;expected_output&gt;
///   Vad Claude ska producera (kod, förklaring, alternativ)
/// &lt;/expected_output&gt;
/// </code>
/// </para>
/// </summary>
internal sealed class ClaudePromptFormatter
{
    public PromptResult Format(PromptRequest request)
    {
        var issue = request.Issue;
        var sb    = new StringBuilder();

        // ── Task ──────────────────────────────────────────────────────────
        sb.AppendLine("<task>");
        sb.AppendLine(BuildTaskDescription(issue));
        sb.AppendLine("</task>");
        sb.AppendLine();

        // ── Context ───────────────────────────────────────────────────────
        sb.AppendLine("<context>");
        AppendContext(sb, request);
        sb.AppendLine("</context>");
        sb.AppendLine();

        // ── Problem Analysis ──────────────────────────────────────────────
        sb.AppendLine("<problem_analysis>");
        AppendProblemAnalysis(sb, issue);
        sb.AppendLine("</problem_analysis>");
        sb.AppendLine();

        // ── Problematic Code ──────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(issue.Snippet))
        {
            sb.AppendLine("<code>");
            sb.AppendLine($"// File: {issue.FilePath} | Line: {issue.StartLine}–{issue.EndLine}");
            sb.AppendLine($"// Scope: {PromptBuilder.BuildScopePath(issue)}");
            sb.AppendLine(PromptBuilder.CodeBlock(issue.Snippet));
            sb.AppendLine("</code>");
            sb.AppendLine();
        }

        // ── Requirements ──────────────────────────────────────────────────
        sb.AppendLine("<requirements>");
        AppendRequirements(sb, issue, request);
        sb.AppendLine("</requirements>");
        sb.AppendLine();

        // ── Constraints ───────────────────────────────────────────────────
        sb.AppendLine("<constraints>");
        AppendConstraints(sb, request);
        sb.AppendLine("</constraints>");
        sb.AppendLine();

        // ── Related Files (om det finns) ──────────────────────────────────
        if (request.RelatedFiles?.Count > 0)
        {
            sb.AppendLine("<related_files>");
            foreach (var file in request.RelatedFiles)
            {
                sb.AppendLine($"<!-- {file.FilePath}{(file.Description != null ? " — " + file.Description : "")} -->");
                sb.AppendLine(PromptBuilder.CodeBlock(file.Content));
            }
            sb.AppendLine("</related_files>");
            sb.AppendLine();
        }

        // ── Expected Output ───────────────────────────────────────────────
        sb.AppendLine("<expected_output>");
        AppendExpectedOutput(sb, issue);
        sb.AppendLine("</expected_output>");

        return new PromptResult
        {
            PromptText        = sb.ToString().TrimEnd(),
            Target            = PromptTarget.Claude,
            RuleId            = issue.RuleId,
            SuggestedCommands = []
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Sektionsbyggare
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildTaskDescription(RawIssue issue) => issue.RuleId switch
    {
        var id when id.StartsWith("CA006") =>
            $"Implement the method `{issue.Scope.MemberName}` in `{issue.FilePath}` " +
            $"which currently throws `NotImplementedException`. " +
            $"Infer the correct implementation from the method signature, XML documentation, " +
            $"and surrounding class context.",

        var id when id.StartsWith("CA007") =>
            $"Refactor `{issue.FilePath}` to follow the Single File / Single Type principle. " +
            $"The file contains {PromptBuilder.GetMeta(issue, "class_count", "multiple")} type declarations " +
            $"and each secondary type should be extracted to its own file.",

        var id when id.StartsWith("CA008") =>
            $"Decompose the complex method `{issue.Scope.MemberName}` in `{issue.FilePath}`. " +
            $"The method has a complexity score of {PromptBuilder.GetMeta(issue, "complexity", "?")} " +
            $"and should be split into smaller, single-responsibility methods " +
            $"without changing the observable behavior.",

        _ =>
            $"Fix the {issue.Category} issue [{issue.RuleId}] " +
            $"in `{issue.FilePath}:{issue.StartLine}`: {issue.Message}"
    };

    private static void AppendContext(StringBuilder sb, PromptRequest request)
    {
        var issue = request.Issue;

        // Tech stack
        if (!string.IsNullOrWhiteSpace(request.ProjectTechStack))
            sb.AppendLine($"**Tech Stack:** {request.ProjectTechStack}");
        else
            sb.AppendLine("**Tech Stack:** C# / .NET (inferred from file extension)");

        // Architecture
        if (!string.IsNullOrWhiteSpace(request.ArchitecturePattern))
            sb.AppendLine($"**Architecture:** {request.ArchitecturePattern}");

        // Rule metadata
        sb.AppendLine($"**Rule:** [{issue.RuleId}] {issue.Category} — {PromptBuilder.FormatSeverity(issue.Severity)}");
        sb.AppendLine($"**Location:** {PromptBuilder.FormatLocation(issue)}");
        sb.AppendLine($"**Scope path:** {PromptBuilder.BuildScopePath(issue)}");

        // BacklogItem history (om det finns)
        if (request.ExistingBacklogItem is { } item)
        {
            sb.AppendLine();
            sb.AppendLine($"**BacklogItem history:**");
            sb.AppendLine($"  - Status: {item.Status}");
            sb.AppendLine($"  - Registered: {item.CreatedAt:yyyy-MM-dd}");
            if (item.LastSeenInSessionId.HasValue)
                sb.AppendLine($"  - Last confirmed: session {item.LastSeenInSessionId.Value:N[..8]}…");
        }
    }

    private static void AppendProblemAnalysis(StringBuilder sb, RawIssue issue)
    {
        sb.AppendLine($"**Detected problem:** {issue.Message}");
        sb.AppendLine();

        switch (issue.RuleId)
        {
            case var id when id.StartsWith("CA006"):
                sb.AppendLine(
                    "A `throw new NotImplementedException()` is a placeholder that will crash at runtime. " +
                    "It indicates unfinished work that needs a real implementation. " +
                    "The risk: if this code path is hit in production (e.g., via an interface contract " +
                    "or inherited method), it causes an unhandled exception.");
                sb.AppendLine();
                sb.AppendLine("**Analysis approach:**");
                sb.AppendLine("1. Read the method's XML summary doc (if present) for intent.");
                sb.AppendLine("2. Look at the method signature: parameter types reveal expected inputs/outputs.");
                sb.AppendLine("3. Check if other methods in the class show the expected pattern.");
                sb.AppendLine("4. Check the interface this class implements (if any) for contract docs.");
                break;

            case var id when id.StartsWith("CA007"):
                var classCount = PromptBuilder.GetMeta(issue, "class_count", "multiple");
                var typeNames  = PromptBuilder.GetMeta(issue, "extra_types", "");
                sb.AppendLine(
                    $"The file contains {classCount} type declarations, which violates the " +
                    "Single Responsibility Principle at the file level. " +
                    "Multiple types in one file makes navigation, testing and code review harder.");
                if (!string.IsNullOrEmpty(typeNames))
                {
                    sb.AppendLine();
                    sb.AppendLine($"**Types to extract:** `{typeNames}`");
                }
                sb.AppendLine();
                sb.AppendLine("**Extraction rules:**");
                sb.AppendLine("- Primary type (same name as file) stays in the current file.");
                sb.AppendLine("- Each secondary type gets its own file: `TypeName.cs` in the same directory.");
                sb.AppendLine("- Namespace declarations stay identical — no logic changes.");
                break;

            case var id when id.StartsWith("CA008"):
                var complexity  = PromptBuilder.GetMeta(issue, "complexity", "?");
                var lineCount   = PromptBuilder.GetMeta(issue, "line_count", "?");
                var returnCount = PromptBuilder.GetMeta(issue, "return_count", "?");
                sb.AppendLine(
                    $"The method `{issue.Scope.MemberName}` has a complexity score of {complexity} " +
                    $"and spans {lineCount} lines with {returnCount} return paths. " +
                    "High complexity increases bug probability, makes unit testing harder, " +
                    "and slows code reviews.");
                sb.AppendLine();
                sb.AppendLine("**Refactoring strategy:**");
                sb.AppendLine("- Identify distinct logical phases in the method body.");
                sb.AppendLine("- Extract each phase into a private helper method with a descriptive name.");
                sb.AppendLine("- The public method becomes an orchestration shell (≤20 lines).");
                sb.AppendLine("- Preserve all existing unit test coverage.");
                break;

            default:
                sb.AppendLine(issue.Suggestion ?? $"See rule documentation for [{issue.RuleId}].");
                break;
        }
    }

    private static void AppendRequirements(StringBuilder sb, RawIssue issue, PromptRequest request)
    {
        // Shared requirements
        sb.AppendLine("**All solutions MUST:**");
        sb.AppendLine("- [ ] Compile without errors or warnings.");
        sb.AppendLine("- [ ] Preserve all existing public API contracts (no breaking changes).");
        sb.AppendLine("- [ ] Not change any observable behavior except the one described.");
        sb.AppendLine("- [ ] Include XML summary documentation for new public members.");
        sb.AppendLine();

        // Rule-specific requirements
        sb.AppendLine("**Rule-specific requirements:**");
        switch (issue.RuleId)
        {
            case var id when id.StartsWith("CA006"):
                sb.AppendLine("- [ ] Replace `throw new NotImplementedException()` with a real implementation.");
                sb.AppendLine("- [ ] Handle edge cases: null inputs, empty collections, invalid states.");
                sb.AppendLine("- [ ] If the correct implementation is unclear, add a `// TODO: [reason]` " +
                              "comment and implement a safe default (return null, empty list, etc.) " +
                              "instead of throwing.");
                if (issue.IsAutoFixable && !string.IsNullOrWhiteSpace(issue.FixedSnippet))
                {
                    sb.AppendLine();
                    sb.AppendLine("**Suggested starter code:**");
                    sb.AppendLine(PromptBuilder.CodeBlock(issue.FixedSnippet));
                }
                break;

            case var id when id.StartsWith("CA007"):
                sb.AppendLine("- [ ] Extract each non-primary type to its own `.cs` file.");
                sb.AppendLine("- [ ] File name must match the type name exactly.");
                sb.AppendLine("- [ ] No `using` directives should be lost in the split.");
                sb.AppendLine("- [ ] Update any `partial` keyword usage correctly.");
                break;

            case var id when id.StartsWith("CA008"):
                var maxComplexity = PromptBuilder.GetMeta(issue, "max_complexity", "10");
                sb.AppendLine($"- [ ] Reduce cyclomatic complexity to ≤ {maxComplexity}.");
                sb.AppendLine("- [ ] Each extracted method: single responsibility, ≤ 30 lines.");
                sb.AppendLine("- [ ] Private helper methods are not required to have XML docs (but may).");
                sb.AppendLine("- [ ] All existing unit tests must still pass without modification.");
                break;

            default:
                if (!string.IsNullOrWhiteSpace(issue.Suggestion))
                    sb.AppendLine($"- [ ] {issue.Suggestion}");
                break;
        }
    }

    private static void AppendConstraints(StringBuilder sb, PromptRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ProjectTechStack))
            sb.AppendLine($"- **Framework:** {request.ProjectTechStack} — do not introduce incompatible dependencies.");

        if (!string.IsNullOrWhiteSpace(request.ArchitecturePattern))
            sb.AppendLine($"- **Architecture:** Follow the {request.ArchitecturePattern} pattern. " +
                          "Do not create cross-layer dependencies.");

        if (!string.IsNullOrWhiteSpace(request.CodingConventions))
            sb.AppendLine($"- **Code style:** {request.CodingConventions}");

        sb.AppendLine("- **Backwards compatibility:** Do not rename, move or delete existing public members.");
        sb.AppendLine("- **Minimal diff:** Make the smallest change that satisfies the requirements.");
        sb.AppendLine("- **No third-party packages:** Do not add new NuGet packages unless explicitly asked.");
    }

    private static void AppendExpectedOutput(StringBuilder sb, RawIssue issue)
    {
        sb.AppendLine("Provide:");
        sb.AppendLine("1. **The fixed code** — complete and ready to replace the original block.");
        sb.AppendLine("2. **Brief explanation** (3–5 sentences) of the design decision made.");

        if (issue.RuleId.StartsWith("CA007"))
        {
            sb.AppendLine("3. **File list** — the names of all new files to create.");
        }
        else if (issue.RuleId.StartsWith("CA008"))
        {
            sb.AppendLine("3. **Method map** — a table showing old method → extracted helper methods.");
        }
        else if (issue.RuleId.StartsWith("CA006"))
        {
            sb.AppendLine("3. **Assumptions made** — list any assumptions about the intended behavior.");
        }

        sb.AppendLine();
        sb.AppendLine("Format the code in a single fenced code block. Do not use multiple separate blocks.");
    }
}
