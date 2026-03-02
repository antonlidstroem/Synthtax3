using System.Text;
using Synthtax.Core.PromptFactory;
using Synthtax.Domain.Enums;

namespace Synthtax.Application.PromptFactory;

/// <summary>
/// Förvandlar ett kodfel till en redo-att-använda AI-instruktion.
///
/// <para><b>Copilot-format (kompakt):</b>
/// <code>
/// // Copilot: Fix NotImplementedException in PaymentService.ProcessRefund
/// // File: src/Services/PaymentService.cs:42
/// // Rule: SA001 — NotImplementedException detected
/// // Task: Replace the NotImplementedException with a real implementation.
/// //       The method signature is: Task&lt;RefundResult&gt; ProcessRefund(string orderId)
/// //       Implement proper business logic and error handling.
/// </code>
/// </para>
///
/// <para><b>Claude-format (fullständig Technical Spec):</b>
/// Inkluderar: sammanhang, regel-förklaring, kodsnippe, arkitektonisk kontext,
/// begränsningar och förväntad output-form.
/// </para>
/// </summary>
public sealed class PromptFactoryService : IPromptFactoryService
{
    // ═══════════════════════════════════════════════════════════════════════
    // IPromptFactoryService
    // ═══════════════════════════════════════════════════════════════════════

    public GeneratedPrompt Generate(PromptContext ctx, PromptTarget target) => target switch
    {
        PromptTarget.Copilot => RenderCopilot(ctx),
        PromptTarget.Claude  => RenderClaude(ctx),
        PromptTarget.Generic => RenderGeneric(ctx),
        _                    => RenderGeneric(ctx)
    };

    public (GeneratedPrompt Copilot, GeneratedPrompt Claude) GenerateBoth(PromptContext ctx) =>
        (RenderCopilot(ctx), RenderClaude(ctx));

    public IReadOnlyList<GeneratedPrompt> GenerateBatch(
        IReadOnlyList<PromptContext> contexts,
        PromptTarget                target)
    {
        return contexts
            .OrderByDescending(c => (int)c.Severity)
            .Select(c => Generate(c, target))
            .ToList()
            .AsReadOnly();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Copilot-renderare
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kompakta inline-kommentarer optimerade för GitHub Copilot i editorn.
    /// Mål: ~50–150 tokens. Fokus på "vad" och "var", minimal bakgrundstext.
    /// </summary>
    private static GeneratedPrompt RenderCopilot(PromptContext ctx)
    {
        var scope  = BuildScopeLabel(ctx);
        var lang   = DetectCommentPrefix(ctx.FilePath);
        var sb     = new StringBuilder();

        // Rubrik
        sb.AppendLine($"{lang} Copilot: {BuildActionVerb(ctx.RuleId)} in {scope}");
        sb.AppendLine($"{lang} File: {ctx.FilePath}:{ctx.StartLine}");
        sb.AppendLine($"{lang} Rule: {ctx.RuleId} — {ctx.RuleName} [{ctx.Severity}]");
        sb.AppendLine($"{lang}");

        // Kärnuppgift — kortfattad och handlingsorienterad
        sb.AppendLine($"{lang} Task: {BuildCopilotTask(ctx)}");

        // Kodsnippe om den är kort nog
        if (!string.IsNullOrWhiteSpace(ctx.Snippet) && ctx.Snippet.Length <= 300)
        {
            sb.AppendLine($"{lang}");
            sb.AppendLine($"{lang} Current code:");
            foreach (var line in ctx.Snippet.Split('\n').Take(8))
                sb.AppendLine($"{lang}   {line.TrimEnd()}");
        }

        // Auto-fix hint
        if (ctx.IsAutoFixable && !string.IsNullOrWhiteSpace(ctx.FixedSnippet))
        {
            sb.AppendLine($"{lang}");
            sb.AppendLine($"{lang} Suggested replacement:");
            foreach (var line in ctx.FixedSnippet.Split('\n').Take(8))
                sb.AppendLine($"{lang}   {line.TrimEnd()}");
        }

        return new GeneratedPrompt
        {
            Target  = PromptTarget.Copilot,
            Content = sb.ToString().TrimEnd(),
            RuleId  = ctx.RuleId,
            Title   = $"[Copilot] {ctx.RuleId}: {scope}"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Claude-renderare
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fullständig "Technical Spec" för Claude.
    /// Inkluderar arkitektonisk kontext, design-rationale och förväntad output-form.
    /// Mål: ~300–800 tokens. Optimerad för Claude 3.5 Sonnet och uppåt.
    /// </summary>
    private static GeneratedPrompt RenderClaude(PromptContext ctx)
    {
        var scope = BuildScopeLabel(ctx);
        var sb    = new StringBuilder();

        // ── Rubrik ────────────────────────────────────────────────────────
        sb.AppendLine($"## Code Review Task — {ctx.RuleId}: {ctx.RuleName}");
        sb.AppendLine();

        // ── Sammanhang ────────────────────────────────────────────────────
        sb.AppendLine("### Context");
        if (!string.IsNullOrEmpty(ctx.ProjectName))
            sb.AppendLine($"- **Project:** {ctx.ProjectName}");
        if (!string.IsNullOrEmpty(ctx.Language))
            sb.AppendLine($"- **Language:** {ctx.Language}");
        sb.AppendLine($"- **File:** `{ctx.FilePath}` (line {ctx.StartLine}–{ctx.EndLine})");
        sb.AppendLine($"- **Scope:** `{scope}`");
        sb.AppendLine($"- **Category:** {ctx.Category}");
        sb.AppendLine($"- **Severity:** {ctx.Severity}");
        if (ctx.SameRuleOpenCount > 1)
            sb.AppendLine($"- **Note:** {ctx.SameRuleOpenCount} open issues with this rule in the project.");
        sb.AppendLine();

        // ── Regelförklaring ───────────────────────────────────────────────
        sb.AppendLine("### Problem");
        sb.AppendLine(ctx.RuleDescription);
        sb.AppendLine();

        // ── Kodsnippe ─────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(ctx.Snippet))
        {
            var ext = Path.GetExtension(ctx.FilePath).TrimStart('.').ToLowerInvariant();
            sb.AppendLine("### Affected Code");
            sb.AppendLine($"```{ext}");
            sb.AppendLine(ctx.Snippet.TrimEnd());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // ── Instruktion ───────────────────────────────────────────────────
        sb.AppendLine("### Your Task");
        sb.AppendLine(BuildClaudeTask(ctx));
        sb.AppendLine();

        // ── Begränsningar ─────────────────────────────────────────────────
        sb.AppendLine("### Constraints");
        sb.AppendLine(BuildClaudeConstraints(ctx));
        sb.AppendLine();

        // ── Förväntat output ──────────────────────────────────────────────
        sb.AppendLine("### Expected Output");
        if (ctx.IsAutoFixable && !string.IsNullOrWhiteSpace(ctx.FixedSnippet))
        {
            var ext = Path.GetExtension(ctx.FilePath).TrimStart('.').ToLowerInvariant();
            sb.AppendLine("Apply the following fix (already validated by the analysis engine):");
            sb.AppendLine($"```{ext}");
            sb.AppendLine(ctx.FixedSnippet.TrimEnd());
            sb.AppendLine("```");
        }
        else if (!string.IsNullOrEmpty(ctx.Suggestion))
        {
            sb.AppendLine(ctx.Suggestion);
        }
        else
        {
            sb.AppendLine(BuildClaudeOutputSpec(ctx));
        }

        // ── Relaterade issues ─────────────────────────────────────────────
        if (ctx.RelatedRuleIds.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Related Issues in This File");
            foreach (var ruleId in ctx.RelatedRuleIds.Take(5))
                sb.AppendLine($"- {ruleId}");
        }

        return new GeneratedPrompt
        {
            Target  = PromptTarget.Claude,
            Content = sb.ToString().TrimEnd(),
            RuleId  = ctx.RuleId,
            Title   = $"[Claude] {ctx.RuleId}: {scope}"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generic-renderare
    // ═══════════════════════════════════════════════════════════════════════

    private static GeneratedPrompt RenderGeneric(PromptContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**{ctx.RuleId} — {ctx.RuleName}** [{ctx.Severity}]");
        sb.AppendLine($"File: `{ctx.FilePath}:{ctx.StartLine}`");
        sb.AppendLine();
        sb.AppendLine(ctx.RuleDescription);
        if (!string.IsNullOrEmpty(ctx.Suggestion))
        {
            sb.AppendLine();
            sb.AppendLine($"**Suggestion:** {ctx.Suggestion}");
        }

        return new GeneratedPrompt
        {
            Target  = PromptTarget.Generic,
            Content = sb.ToString().TrimEnd(),
            RuleId  = ctx.RuleId,
            Title   = $"{ctx.RuleId}: {ctx.RuleName}"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Textbyggare per regel-kategori
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildCopilotTask(PromptContext ctx) => ctx.RuleId switch
    {
        var id when id.StartsWith("SA001") => // NotImplementedException
            $"Replace the NotImplementedException stub with a real implementation. " +
            $"Method: {ctx.MemberName ?? "unknown"}. " +
            $"Implement proper business logic, error handling, and return the correct type.",

        var id when id.StartsWith("SA002") => // MultiClassFile
            $"Extract each class/interface/enum into its own file. " +
            $"Keep the same namespace. " +
            $"File currently contains multiple type declarations.",

        var id when id.StartsWith("SA003") => // ComplexMethod
            $"Decompose the method '{ctx.MemberName}' into smaller, single-responsibility methods. " +
            $"Each extracted method should be ≤15 lines and have a clear, descriptive name.",

        _ =>
            $"{ctx.Suggestion ?? ctx.RuleDescription} " +
            $"Apply the fix at {ctx.FilePath}:{ctx.StartLine}."
    };

    private static string BuildClaudeTask(PromptContext ctx) => ctx.RuleId switch
    {
        var id when id.StartsWith("SA001") =>
            $"""
            The method `{ctx.MemberName ?? "unknown"}` in `{ctx.ClassName}` currently throws a
            `NotImplementedException`, indicating it is a stub that was never completed.

            Your task is to implement this method correctly:
            1. Analyze the method signature, return type, and surrounding class context.
            2. Infer the intended behavior from the method name, parameters, and XML doc comments.
            3. Implement robust business logic with proper null-checking and error handling.
            4. Return a value that satisfies the declared return type contract.
            5. If you cannot determine the full implementation, provide a commented skeleton with
               `TODO` markers explaining what each section should do.
            """,

        var id when id.StartsWith("SA002") =>
            $"""
            The file `{ctx.FilePath}` contains multiple top-level type declarations, violating the
            Single Responsibility Principle and the one-type-per-file convention.

            Your task:
            1. Identify all type declarations in the file (classes, interfaces, records, enums, delegates).
            2. For each secondary type, determine the correct extraction target filename (e.g. `TypeName.cs`).
            3. Produce the refactored file contents — one file per type.
            4. Preserve all `using` directives, namespace declarations, and XML doc comments.
            5. Ensure no circular dependencies are introduced by the extraction.
            6. If any types are tightly coupled (e.g. nested types that share private state), explain
               why they should remain together and propose an alternative refactoring.
            """,

        var id when id.StartsWith("SA003") =>
            $"""
            The method `{ctx.MemberName ?? "unknown"}` in `{ctx.ClassName}` has high cyclomatic
            complexity and/or excessive length, making it difficult to read, test, and maintain.

            Your task:
            1. Identify logical sub-operations within the method (initialization, validation,
               core logic, side effects, cleanup).
            2. Extract each sub-operation into a private helper method with a descriptive name
               following the "verb + noun" convention (e.g. `ValidateOrderItems`, `ApplyDiscount`).
            3. Each extracted method should:
               - Have a single, clear responsibility.
               - Be ≤15 lines long.
               - Accept only the parameters it actually needs.
               - Return a meaningful value or be void with clear side effects.
            4. The original method should become a high-level orchestrator that reads like prose.
            5. Do not change the observable behavior of the code.
            """,

        _ =>
            ctx.Suggestion
            ?? $"Fix the issue described above. {ctx.RuleDescription}"
    };

    private static string BuildClaudeConstraints(PromptContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("- Do not change method signatures visible to external callers.");
        sb.AppendLine("- Maintain existing XML documentation comments.");
        sb.AppendLine("- Follow the code style visible in the snippet (naming conventions, brace style).");

        if (ctx.Language?.Contains("C#", StringComparison.OrdinalIgnoreCase) == true)
        {
            sb.AppendLine("- Use C# nullable reference types correctly (`?` annotations).");
            sb.AppendLine("- Prefer `async/await` over `.Result` or `.Wait()`.");
            sb.AppendLine("- Use `ArgumentNullException.ThrowIfNull()` for null checks (C# 10+).");
        }
        if (ctx.Language?.Contains("Python", StringComparison.OrdinalIgnoreCase) == true)
        {
            sb.AppendLine("- Add type hints to all function parameters and return types.");
            sb.AppendLine("- Follow PEP 8 naming conventions.");
        }
        if (ctx.Language?.Contains("JavaScript", StringComparison.OrdinalIgnoreCase) == true ||
            ctx.Language?.Contains("TypeScript", StringComparison.OrdinalIgnoreCase) == true)
        {
            sb.AppendLine("- Use `const`/`let`, never `var`.");
            sb.AppendLine("- Prefer `async/await` over raw Promise chains.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildClaudeOutputSpec(PromptContext ctx) => ctx.RuleId switch
    {
        var id when id.StartsWith("SA001") =>
            "Provide the complete, implemented method body as a code block. " +
            "Include brief inline comments for non-obvious logic.",

        var id when id.StartsWith("SA002") =>
            "Provide each extracted file as a separate code block, clearly labelled with its filename. " +
            "Include the full file content including namespace and using directives.",

        var id when id.StartsWith("SA003") =>
            "Provide the refactored class section as a single code block. " +
            "Show the simplified original method and all extracted helper methods.",

        _ =>
            "Provide the corrected code as a code block. " +
            "If multiple approaches exist, briefly explain the trade-offs."
    };

    // ═══════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildScopeLabel(PromptContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.MemberName) && !string.IsNullOrEmpty(ctx.ClassName))
            return $"{ctx.ClassName}.{ctx.MemberName}";
        if (!string.IsNullOrEmpty(ctx.ClassName))
            return ctx.ClassName;
        return Path.GetFileName(ctx.FilePath);
    }

    private static string BuildActionVerb(string ruleId) => ruleId switch
    {
        var id when id.StartsWith("SA001") => "Implement stub method",
        var id when id.StartsWith("SA002") => "Extract types into separate files",
        var id when id.StartsWith("SA003") => "Decompose complex method",
        _                                  => "Fix issue"
    };

    private static string DetectCommentPrefix(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".py" or ".rb" or ".sh" or ".yaml" or ".yml" => "#",
            ".html" or ".xml"                            => "<!--",
            _                                            => "//"  // C#/Java/JS/TS default
        };
    }
}
