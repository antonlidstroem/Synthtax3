using Microsoft.VisualStudio.Shell;
using System.Windows;

namespace Synthtax.Vsix.Services;

/// <summary>
/// Skickar AI-promptar till rätt mottagare inuti Visual Studio.
///
/// <para><b>Copilot-strategi:</b>
/// VS 2022 17.6+ exponerar Copilot Chat via
/// <c>Microsoft.VisualStudio.Copilot.Contracts.ICopilotChatService</c>.
/// Eftersom det kontraktet är preview och kräver en specifik SDK-version
/// använder vi en defensiv "try-reflect, fallback to clipboard"-approach
/// för att vara bakåtkompatibla med VS-versioner utan Copilot.</para>
///
/// <para><b>Fallback-ordning:</b>
/// <list type="number">
///   <item>Försök: Copilot IChatService via MEF (VS 17.8+).</item>
///   <item>Fallback: Öppna Copilot Chat-fönster + sätt urklipp (VS 17.6+).</item>
///   <item>Sista fallback: Sätt urklipp + visa statusbar-meddelande.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PromptDispatchService
{
    // ═══════════════════════════════════════════════════════════════════════
    // Copilot
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Skickar <paramref name="promptText"/> till GitHub Copilot Inline Chat.
    /// Faller tillbaka på urklipp om Copilot-API:t inte är tillgängligt.
    /// </summary>
    public async Task SendToCopilotAsync(string promptText, CancellationToken ct = default)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        // Steg 1: Försök att injicera via Copilot MEF-tjänst (VS 17.8+)
        if (await TrySendViaCopilotServiceAsync(promptText, ct))
            return;

        // Steg 2: Öppna Copilot Chat-fönstret och klistra in
        if (await TryOpenCopilotWindowWithTextAsync(promptText, ct))
            return;

        // Steg 3: Urklipp-fallback
        Clipboard.SetText(promptText);
        await ShowStatusBarAsync(
            "⚡ Copilot-prompt kopierad. Öppna Copilot Chat (Alt+/) och klistra in.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Privata implementationer
    // ═══════════════════════════════════════════════════════════════════════

    private static async Task<bool> TrySendViaCopilotServiceAsync(
        string text, CancellationToken ct)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            // Reflection-baserad approach — undviker hårt beroende på Copilot-assemblyt
            // Assembly: Microsoft.VisualStudio.Copilot.Contracts
            // Interface: ICopilotChatService
            // Method: SubmitAsync(string query, CancellationToken ct)

            var copilotAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name ==
                    "Microsoft.VisualStudio.Copilot.Contracts");

            if (copilotAssembly is null) return false;

            var serviceType = copilotAssembly
                .GetType("Microsoft.VisualStudio.Copilot.ICopilotChatService");
            if (serviceType is null) return false;

            var service = Microsoft.VisualStudio.Shell.Package
                .GetGlobalService(serviceType);
            if (service is null) return false;

            var submitMethod = serviceType.GetMethod("SubmitAsync",
                new[] { typeof(string), typeof(CancellationToken) });
            if (submitMethod is null) return false;

            await (Task)submitMethod.Invoke(service, new object[] { text, ct })!;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryOpenCopilotWindowWithTextAsync(
        string text, CancellationToken ct)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

            // Försök aktivera kommandot "View.GitHubCopilotChat"
            var dte = (EnvDTE.DTE?)Microsoft.VisualStudio.Shell.Package
                .GetGlobalService(typeof(EnvDTE.DTE));

            if (dte is null) return false;

            // Öppna Copilot Chat-fönstret
            dte.ExecuteCommand("View.GitHubCopilotChat");

            // Ge fönstret tid att öppnas
            await Task.Delay(300, ct);

            // Sätt texten i urklipp så användaren kan Ctrl+V direkt i chat
            Clipboard.SetText(text);

            await ShowStatusBarAsync(
                "⚡ Copilot Chat öppnat. Klistra in (Ctrl+V) prompten.");

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ShowStatusBarAsync(string message)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var statusBar = (Microsoft.VisualStudio.Shell.Interop.IVsStatusbar?)
            Microsoft.VisualStudio.Shell.Package.GetGlobalService(
                typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar));
        statusBar?.SetText(message);
    }
}
