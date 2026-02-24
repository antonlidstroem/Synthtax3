using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class LoginWindow : Window
{
    public event EventHandler? LoginSucceeded;

    public LoginWindow(IServiceProvider services)
    {
        InitializeComponent();

        var vm = services.GetRequiredService<LoginViewModel>();
        vm.LoginSucceeded += (_, _) => LoginSucceeded?.Invoke(this, EventArgs.Empty);
        DataContext = vm;

        // Wire PasswordBox manually (can't bind SecureString in WPF easily)
        PasswordBox.PasswordChanged += (_, _) => vm.Password = PasswordBox.Password;

        // Enter key on username focuses password
        UsernameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) PasswordBox.Focus();
        };

        // Enter key on password triggers login
        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return && vm.LoginCommand.CanExecute(null))
                vm.LoginCommand.Execute(null);
        };
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void OnClose(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}
