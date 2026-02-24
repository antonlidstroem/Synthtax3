using System.Windows;
using System.Windows.Input;
using Synthtax.WPF.ViewModels.Dialogs;

namespace Synthtax.WPF.Views.Dialogs;

public partial class AdminCreateUserDialog : Window
{
    public AdminCreateUserDialog(AdminCreateUserDialogViewModel vm, Window owner)
    {
        InitializeComponent();
        DataContext = vm;
        Owner = owner;

        PasswordBox.PasswordChanged += (_, _) => vm.Password = PasswordBox.Password;
        vm.DialogClosed += (_, success) => { DialogResult = success; Close(); };
    }

    private void OnClose(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
