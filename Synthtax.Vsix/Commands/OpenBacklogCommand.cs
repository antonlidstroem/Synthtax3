using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.Package;
using Synthtax.Vsix.ToolWindow;
using Synthtax.Vsix.ToolWindow.ViewModels;

namespace Synthtax.Vsix.Commands;

internal sealed class OpenBacklogCommand
{
    private readonly AsyncPackage _package;

    private OpenBacklogCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var cmdId = new CommandID(
            SynthtaxPackageGuids.CommandSetGuid,
            SynthtaxPackageGuids.OpenBacklogCommandId);
        commandService.AddCommand(new MenuCommand(Execute, cmdId));
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
                typeof(BacklogToolWindow),
                id:     0,
                create: true,
                cancellationToken: CancellationToken.None);
            ((IVsWindowFrame?)window)?.Show();
        });
    }
}

internal sealed class RefreshBacklogCommand
{
    private readonly AsyncPackage _package;

    private RefreshBacklogCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var cmdId = new CommandID(
            SynthtaxPackageGuids.CommandSetGuid,
            SynthtaxPackageGuids.RefreshBacklogCommandId);
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
        // BUGFIX #1: Execute-bodyn var helt tom — knappen gjorde ingenting.
        // Nu hämtar vi ViewModeln korrekt och anropar RefreshCommand.
        _ = _package.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Hämta (eller skapa) tool window-pane
            var pane = await _package.FindToolWindowAsync(
                typeof(BacklogToolWindow),
                id:     0,
                create: false,
                cancellationToken: CancellationToken.None) as BacklogToolWindow;

            if (pane is null) return; // fönstret inte öppet

            // BacklogToolWindow exponerar sin ViewModel via Content
            if (pane.Content is System.Windows.FrameworkElement fe
                && fe.DataContext is BacklogToolWindowViewModel vm)
            {
                if (vm.RefreshCommand.CanExecute(null))
                    await vm.RefreshCommand.ExecuteAsync(null);
            }
        });
    }
}
