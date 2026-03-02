using System.Windows.Controls;
using System.Windows.Input;
using Synthtax.Vsix.ToolWindow.ViewModels;

namespace Synthtax.Vsix.ToolWindow.Views;

/// <summary>
/// Code-behind för <c>BacklogToolWindowControl.xaml</c>.
/// All affärslogik hanteras av <see cref="BacklogToolWindowViewModel"/>.
/// Code-behind innehåller bara UI-specifik "glue" som inte passar i MVVM.
/// </summary>
public partial class BacklogToolWindowControl : UserControl
{
    public BacklogToolWindowControl()
    {
        InitializeComponent();

        // F5 → Refresh
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && DataContext is BacklogToolWindowViewModel vm)
        {
            if (vm.RefreshCommand.CanExecute(null))
                _ = vm.RefreshCommand.ExecuteAsync(null);
            e.Handled = true;
        }
    }
}
