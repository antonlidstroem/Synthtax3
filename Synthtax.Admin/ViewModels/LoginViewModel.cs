using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Synthtax.Admin.Services;
using Synthtax.Admin.Views;

namespace Synthtax.Admin.ViewModels;

public class LoginViewModel : INotifyPropertyChanged
{
    private readonly AuthService _auth;
    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    // Password is passed in via code-behind (PasswordBox is not bindable for security)
    public ICommand LoginCommand { get; }

    public LoginViewModel(AuthService auth)
    {
        _auth = auth;
        LoginCommand = new RelayCommand<string>(async pw => await DoLoginAsync(pw));
    }

    private async Task DoLoginAsync(string? password)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _auth.LoginAsync(Username, password ?? "");
            if (!result.Success)
            {
                ErrorMessage = result.Error;
                return;
            }

            var userSvc = new UserService(_auth.Http);
            var mainVm  = new MainViewModel(_auth, userSvc);
            var main    = new MainView { DataContext = mainVm };
            main.Show();
            Application.Current.Windows[0]?.Close();
        }
        finally { IsBusy = false; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
