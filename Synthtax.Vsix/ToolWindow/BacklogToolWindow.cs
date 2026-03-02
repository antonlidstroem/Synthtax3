using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Package;
using Synthtax.Vsix.ToolWindow.ViewModels;
using Synthtax.Vsix.ToolWindow.Views;

namespace Synthtax.Vsix.ToolWindow;

/// <summary>
/// VS Tool Window host för "Synthtax Backlog".
///
/// <para>Registreras i <see cref="SynthtaxPackage"/> via
/// <c>[ProvideToolWindow(typeof(BacklogToolWindow), ...)]</c>-attributet.</para>
///
/// <para><b>Skapningsordning:</b>
/// <list type="number">
///   <item>VS anropar <see cref="BacklogToolWindow()"/> (parameterless ctor krävs av VS SDK).</item>
///   <item><see cref="Content"/> returnerar WPF <see cref="BacklogToolWindowControl"/>.</item>
///   <item>Kontrollen binder mot en <see cref="BacklogToolWindowViewModel"/>.</item>
/// </list>
/// </para>
/// </summary>
[Guid(SynthtaxPackageGuids.BacklogToolWindowGuidString)]
public sealed class BacklogToolWindow : ToolWindowPane
{
    private BacklogToolWindowViewModel? _viewModel;

    /// <summary>Parameterless constructor — krävs av VS SDK.</summary>
    public BacklogToolWindow() : base(null)
    {
        Caption = "Synthtax Backlog";

        // BitmapResourceID och BitmapIndex pekar på ikon i resources
        // (konfigureras via .vsct och ToolWindowPane.BitmapResourceID)
        BitmapResourceID = 301;
        BitmapIndex      = 1;
    }

    /// <summary>
    /// Anropas av VS när Tool Window-ramen är skapad och paketet är initialiserat.
    /// Här skapar vi ViewModel och knyter den till vyn.
    /// </summary>
    public override void OnToolWindowCreated()
    {
        base.OnToolWindowCreated();

        ThreadHelper.ThrowIfNotOnUIThread();

        // Hämta paketet för DI
        var package = (SynthtaxPackage?)Microsoft.VisualStudio.Shell.Package
            .GetGlobalService(typeof(SynthtaxPackage));

        if (package is not null)
        {
            _viewModel = new BacklogToolWindowViewModel(package);

            // WPF-kontrollen som visas i fönstret
            var control = new BacklogToolWindowControl
            {
                DataContext = _viewModel
            };

            Content = control;

            // Starta auto-refresh om konfigurerat
            _ = TryAutoRefreshAsync();
        }
        else
        {
            // Fallback om paketet inte är redo
            Content = new TextBlock
            {
                Text = "Synthtax package not initialized. Try reopening the window.",
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
        }
    }

    private async Task TryAutoRefreshAsync()
    {
        if (_viewModel?.RefreshCommand.CanExecute(null) == true)
            await _viewModel.RefreshCommand.ExecuteAsync(null);
    }
}
