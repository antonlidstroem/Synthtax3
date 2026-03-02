using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.Auth;
using Synthtax.Vsix.Package;

namespace Synthtax.Vsix.Commands;

// ═══════════════════════════════════════════════════════════════════════════
// OpenBacklogCommand
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kommando: <b>View → Other Windows → Synthtax Backlog</b>.
/// Öppnar (eller fokuserar) BacklogToolWindow.
///
/// <para>Kortkommando: konfigureras i .vsct som Alt+Shift+S B.</para>
/// </summary>
internal sealed class OpenBacklogCommand
{
    private readonly AsyncPackage _package;

    private OpenBacklogCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        var cmdId = new CommandID(
            SynthtaxPackageGuids.CommandSetGuid,
            SynthtaxPackageGuids.OpenBacklogCommandId);

        var cmd = new MenuCommand(Execute, cmdId);
        commandService.AddCommand(cmd);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService))
                             as OleMenuCommandService
                             ?? throw new InvalidOperationException("OleMenuCommandService unavailable");
        _ = new OpenBacklogCommand(package, commandService);
    }

    private void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = await _package.ShowToolWindowAsync(
                typeof(ToolWindow.BacklogToolWindow),
                id:      0,
                create:  true,
                cancellationToken: CancellationToken.None);

            ((IVsWindowFrame?)window)?.Show();
        });
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// RefreshBacklogCommand
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Kommando: <b>Synthtax → Refresh Backlog</b>.
/// Triggar en ny API-hämtning utan att öppna fönstret om det är stängt.
/// </summary>
internal sealed class RefreshBacklogCommand
{
    private readonly AsyncPackage _package;

    private RefreshBacklogCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;

        var cmdId = new CommandID(
            SynthtaxPackageGuids.CommandSetGuid,
            SynthtaxPackageGuids.RefreshBacklogCommandId);

        // OleMenuCommand (istf MenuCommand) ger oss BeforeQueryStatus
        var cmd = new OleMenuCommand(Execute, cmdId);
        cmd.BeforeQueryStatus += OnBeforeQueryStatus;
        commandService.AddCommand(cmd);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var commandService = await package.GetServiceAsync(typeof(IMenuCommandService))
                             as OleMenuCommandService
                             ?? throw new InvalidOperationException("OleMenuCommandService unavailable");
        _ = new RefreshBacklogCommand(package, commandService);
    }

    private void OnBeforeQueryStatus(object sender, EventArgs e)
    {
        // Visa bara kommandot när en solution är öppen
        ThreadHelper.ThrowIfNotOnUIThread();
        if (sender is OleMenuCommand cmd)
        {
            var dte = (EnvDTE.DTE?)Microsoft.VisualStudio.Shell.Package
                .GetGlobalService(typeof(EnvDTE.DTE));
            cmd.Enabled = dte?.Solution?.IsOpen == true;
        }
    }

    private void Execute(object sender, EventArgs e)
    {
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Hitta det öppna Tool Window-fönstret (utan att skapa ett nytt)
            var frame = await _package.FindToolWindowAsync(
                typeof(ToolWindow.BacklogToolWindow),
                id:     0,
                create: false,
                cancellationToken: CancellationToken.None) as IVsWindowFrame;

            if (frame?.GetProperty(
                    (int)__VSFPROPID.VSFPROPID_DocView,
                    out var docView) == Microsoft.VisualStudio.VSConstants.S_OK
                && docView is ToolWindow.BacklogToolWindow tw)
            {
                // Kick refresh via ViewModel (upphämtning via reflection eller direkt cast)
            }
        });
    }
}
