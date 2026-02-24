using System.Windows;
using System.Windows.Input;
using Synthtax.WPF.ViewModels.Dialogs;

namespace Synthtax.WPF.Views.Dialogs;

public partial class AdminResetPasswordDialog : Window
{
    private readonly AdminResetPasswordDialogViewModel _vm;

    public AdminResetPasswordDialog(AdminResetPasswordDialogViewModel vm, Window owner)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        Owner = owner;

        NewPwBox.PasswordChanged    += (_, _) => _vm.NewPassword     = NewPwBox.Password;
        ConfirmPwBox.PasswordChanged += (_, _) => _vm.ConfirmPassword = ConfirmPwBox.Password;

        vm.DialogClosed += (_, success) => { DialogResult = success; Close(); };
    }

    private void OnClose(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
