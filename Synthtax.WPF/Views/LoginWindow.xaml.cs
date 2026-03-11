using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Synthtax.WPF.Views;

public partial class LoginWindow : Window
{
    public event EventHandler? LoginSucceeded;

    public LoginWindow(IServiceProvider services)
    {
        InitializeComponent();

        // Hämta ViewModel från DI
        var vm = services.GetRequiredService<LoginViewModel>();
        DataContext = vm;

        // Koppla event för lyckad inloggning
        vm.LoginSucceeded += (_, _) => LoginSucceeded?.Invoke(this, EventArgs.Empty);

        // Wire PasswordBox manuellt (SecureString-bindning är komplicerad i WPF)
        PasswordBox.PasswordChanged += (_, _) =>
        {
            vm.Password = PasswordBox.Password;
            vm.LoginCommand.NotifyCanExecuteChanged(); // Uppdaterar CanExecute när lösenord ändras
        };

        // Enter-tangent i UsernameBox fokuserar PasswordBox
        UsernameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return)
                PasswordBox.Focus();
        };

        // Enter-tangent i PasswordBox triggar login
        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return && vm.LoginCommand.CanExecute(null))
                vm.LoginCommand.Execute(null);
        };
    }

    // Gör fönstret dragbart via mus
    private void OnDragWindow(object sender, MouseButtonEventArgs e)
        => DragMove();

    // Stänger applikationen
    private void OnClose(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}