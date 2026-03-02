using System.Collections.Immutable;
using System.Composition;
using System.Windows;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Analyzers;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.Package;
using Synthtax.Vsix.Services;

namespace Synthtax.Vsix.CodeFixes;

/// <summary>
/// Roslyn CodeFixProvider — lägger till "lightbulb"-åtgärder för alla SX-diagnostik.
///
/// <para><b>Tillgängliga åtgärder:</b>
/// <list type="bullet">
///   <item><b>Fix with Copilot</b> — skickar kompakt prompt till VS Copilot inline-chat.</item>
///   <item><b>Export for Claude</b> — kopierar fullständig Technical Spec till urklipp.</item>
///   <item><b>View in Synthtax Backlog</b> — öppnar Tool Window och markerar ärendet.</item>
/// </list>
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SynthtaxCodeFixProvider))]
[Shared]
public sealed class SynthtaxCodeFixProvider : CodeFixProvider
{
    // ── Diagnostik som vi erbjuder fix för ───────────────────────────────
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            "SX0001", "SX0002", "SX0003",
            "SX9001", "SX9002", "SX9003");

    // Strategin: tillhandahåll åtgärder per dokument (inte batch)
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics.First();
        var diagId     = diagnostic.Id;

        // Hämta paketreferens för att komma åt API-klient och PromptDispatch
        var package = await GetPackageAsync();

        // Hitta matchande BacklogItem via diagnostic-location → filsökväg
        var filePath    = context.Document.FilePath ?? "";
        var startLine   = diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;
        var matchedItem = FindMatchingItem(filePath, startLine, diagId);

        if (matchedItem is null && package is null)
        {
            // Ingen koppling till API — visa bara "View in Backlog" med info
            RegisterInfoAction(context, diagnostic);
            return;
        }

        // ── Fix with Copilot ──────────────────────────────────────────────
        context.RegisterCodeFix(
            CodeAction.Create(
                title:             "⚡ Fix with Copilot (Synthtax)",
                createChangedDocument: ct => FixWithCopilotAsync(context.Document, matchedItem, package, ct),
                equivalenceKey:    "Synthtax.FixWithCopilot"),
            diagnostic);

        // ── Export for Claude ─────────────────────────────────────────────
        context.RegisterCodeFix(
            CodeAction.Create(
                title:             "📋 Export for Claude (Synthtax)",
                createChangedDocument: ct => ExportForClaudeAsync(context.Document, matchedItem, package, ct),
                equivalenceKey:    "Synthtax.ExportForClaude"),
            diagnostic);

        // ── View in Backlog ───────────────────────────────────────────────
        context.RegisterCodeFix(
            CodeAction.Create(
                title:             "🔍 View in Synthtax Backlog",
                createChangedDocument: ct => ViewInBacklogAsync(context.Document, matchedItem, ct),
                equivalenceKey:    "Synthtax.ViewInBacklog"),
            diagnostic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Åtgärdsimplementationer
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<Document> FixWithCopilotAsync(
        Document doc, BacklogItemDto? item, SynthtaxPackage? pkg, CancellationToken ct)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        string promptText;

        if (pkg is not null && item is not null)
        {
            try
            {
                var resp = await pkg.ApiClient.GetPromptAsync(item.Id, "Copilot", ct);
                promptText = resp.Content;
            }
            catch (UnauthorizedException)
            {
                promptText = BuildFallbackCopilotPrompt(item);
            }
            catch { promptText = BuildFallbackCopilotPrompt(item); }
        }
        else
        {
            promptText = "// Synthtax: Log in to get AI-powered fix suggestions.";
        }

        // Skicka till Copilot inline-chat via PromptDispatchService
        if (pkg is not null)
            await pkg.PromptDispatch.SendToCopilotAsync(promptText, ct);
        else
            Clipboard.SetText(promptText);

        return doc; // CodeFix utan dokumentändring — effekten är extern
    }

    private static async Task<Document> ExportForClaudeAsync(
        Document doc, BacklogItemDto? item, SynthtaxPackage? pkg, CancellationToken ct)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        string promptText;

        if (pkg is not null && item is not null)
        {
            try
            {
                var resp = await pkg.ApiClient.GetPromptAsync(item.Id, "Claude", ct);
                promptText = resp.Content;
            }
            catch { promptText = BuildFallbackClaudePrompt(item); }
        }
        else if (item is not null)
        {
            promptText = BuildFallbackClaudePrompt(item);
        }
        else
        {
            promptText = "Synthtax: Log in to get full AI context for this issue.";
        }

        Clipboard.SetText(promptText);

        // Visa bekräftelse i VS status bar
        await ShowStatusBarMessageAsync("✅ Claude prompt copied to clipboard. Paste in claude.ai or the API.");

        return doc;
    }

    private static async Task<Document> ViewInBacklogAsync(
        Document doc, BacklogItemDto? item, CancellationToken ct)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        // Öppna Tool Window
        var shell = (Microsoft.VisualStudio.Shell.Interop.IVsUIShell?)
            Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                typeof(Microsoft.VisualStudio.Shell.Interop.SVsUIShell));

        if (shell is not null)
        {
            var toolWindowGuid = SynthtaxPackageGuids.BacklogToolWindowGuid;
            shell.FindToolWindow(
                (uint)Microsoft.VisualStudio.Shell.Interop.__VSFINDTOOLWIN.FTW_fForceCreate,
                ref toolWindowGuid, out var frame);
            frame?.Show();
        }

        return doc;
    }

    private static void RegisterInfoAction(CodeFixContext context, Diagnostic diagnostic)
    {
        context.RegisterCodeFix(
            CodeAction.Create(
                title:             "🔑 Log in to Synthtax for AI fixes",
                createChangedDocument: ct => Task.FromResult(context.Document),
                equivalenceKey:    "Synthtax.LoginPrompt"),
            diagnostic);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Fallback-prompts (när API inte är tillgängligt)
    // ═══════════════════════════════════════════════════════════════════════

    private static string BuildFallbackCopilotPrompt(BacklogItemDto item) =>
        $"""
        // Synthtax [{item.RuleId}] {item.Severity} — {item.Message}
        // File: {item.FilePath}:{item.StartLine}
        // Class: {item.ClassName ?? "?"} | Member: {item.MemberName ?? "?"}
        //
        // Task: {item.Suggestion ?? "Fix the issue described above."}
        //
        // Current code:
        {Indent(item.Snippet, "// ")}
        """;

    private static string BuildFallbackClaudePrompt(BacklogItemDto item) =>
        $"""
        # Synthtax Technical Spec — {item.RuleId}

        ## Issue
        **Rule:** {item.RuleId} — {item.Message}
        **Severity:** {item.Severity}
        **File:** `{item.FilePath}` (line {item.StartLine})
        **Scope:** `{item.Namespace ?? ""}.{item.ClassName ?? "?"}.{item.MemberName ?? "?"}`

        ## Current Code
        ```csharp
        {item.Snippet}
        ```

        ## Task
        {item.Suggestion ?? "Analyze and fix the issue. Provide a complete, production-ready implementation."}

        ## Constraints
        - Maintain existing method signature and visibility
        - Follow C# conventions and SOLID principles
        - Include XML documentation if the member is public
        - Handle edge cases and null inputs appropriately
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Hjälpmetoder
    // ═══════════════════════════════════════════════════════════════════════

    private static BacklogItemDto? FindMatchingItem(string filePath, int line, string diagId)
    {
        // BacklogItemCache är en statisk cache som uppdateras av ToolWindowViewModel
        return BacklogItemCache.FindByLocation(filePath, line);
    }

    private static async Task<SynthtaxPackage?> GetPackageAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return SynthtaxPackage.Instance;
    }

    private static async Task ShowStatusBarMessageAsync(string message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var statusBar = (Microsoft.VisualStudio.Shell.Interop.IVsStatusbar?)
            Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar));
        statusBar?.SetText(message);
    }

    private static string Indent(string text, string prefix) =>
        string.Join("\n", text.Split('\n').Select(l => prefix + l));
}

/// <summary>
/// Statisk cache: knyter BacklogItem till filsökväg + radnummer.
/// Uppdateras varje gång ToolWindowViewModel laddar backlog.
/// </summary>
internal static class BacklogItemCache
{
    private static readonly List<BacklogItemDto> _items = [];
    private static readonly object _lock = new();

    public static void Update(IEnumerable<BacklogItemDto> items)
    {
        lock (_lock)
        {
            _items.Clear();
            _items.AddRange(items);
        }
    }

    public static BacklogItemDto? FindByLocation(string filePath, int line)
    {
        lock (_lock)
        {
            var normalized = filePath.Replace('\\', '/').ToLowerInvariant();
            return _items.FirstOrDefault(i =>
                i.FilePath.Replace('\\', '/').EndsWith(
                    normalized.Split('/').Last(),
                    StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(i.StartLine - line) <= 2);
        }
    }
}
