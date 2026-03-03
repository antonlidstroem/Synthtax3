using System.Windows;
using Synthtax.Admin.Services;
using Synthtax.Admin.ViewModels;

namespace Synthtax.Admin.Views;

public partial class LoginView : Window
{
    public LoginView()
    {
        InitializeComponent();

        // Konfigurera API-URL har. Byt mot din faktiska URL.
        var auth = new AuthService("https://localhost:5001");
        DataContext = new LoginViewModel(auth);
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var vm = (LoginViewModel)DataContext;
        if (vm.LoginCommand.CanExecute(PasswordBox.Password))
            vm.LoginCommand.Execute(PasswordBox.Password);
    }
}
