using System.Windows;
using System.Windows.Input;
using Synthtax.WPF.ViewModels.Dialogs;

namespace Synthtax.WPF.Views.Dialogs;

public partial class AdminModulesDialog : Window
{
    public AdminModulesDialog(AdminModulesDialogViewModel vm, Window owner)
    {
        InitializeComponent();
        DataContext = vm;
        Owner = owner;
        vm.DialogClosed += (_, success) => { DialogResult = success; Close(); };
    }

    private void OnClose(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
