using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private string _username = string.Empty;
    private string _password = string.Empty;

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public event EventHandler? LoginSucceeded;

    public LoginViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            SetError(L["Login_Error_Invalid"]);
            return;
        }

        await RunSafeAsync(async () =>
        {
            var result = await Api.LoginAsync(new LoginDto
            {
                UserName = Username,
                Password = Password
            });

            if (result is null)
            {
                SetError(L["Login_Error_Invalid"]);
                return;
            }

            // Update language from user preferences
            if (result.User.Preferences?.Language is string lang)
                LocalizationService.Current.CurrentLanguage = lang;

            LoginSucceeded?.Invoke(this, EventArgs.Empty);
        });
    }
}
