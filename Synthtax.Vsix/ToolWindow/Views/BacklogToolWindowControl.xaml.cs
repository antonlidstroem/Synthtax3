using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Synthtax.Vsix.ToolWindow.ViewModels;

namespace Synthtax.Vsix.ToolWindow.Views;

public partial class BacklogToolWindowControl : UserControl
{
    public BacklogToolWindowControl()
    {
        InitializeComponent();
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

    // FÖRBÄTTRING #9: Severity-filtret är nu segment-knappar (RadioButton)
    // istället för en ComboBox. Click-event sätter FilterSeverity på ViewModel.
    private void SeverityFilter_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is RadioButton rb
            && rb.Tag is string tag
            && DataContext is BacklogToolWindowViewModel vm)
        {
            vm.FilterSeverity = tag;
        }
    }
}
