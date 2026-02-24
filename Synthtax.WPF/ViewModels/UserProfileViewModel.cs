using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class UserProfileViewModel : ViewModelBase
{
    private string _fullName = string.Empty, _email = string.Empty, _userName = string.Empty;
    private string _role = string.Empty, _theme = "Light", _language = "sv-SE";
    private bool _emailNotifications, _passwordChanged;
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword     { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    public string FullName { get => _fullName; set => SetProperty(ref _fullName, value); }
    public string Email    { get => _email;    set => SetProperty(ref _email, value); }
    public string UserName { get => _userName; private set => SetProperty(ref _userName, value); }
    public string Role     { get => _role;     private set => SetProperty(ref _role, value); }
    public string Theme    { get => _theme;    set => SetProperty(ref _theme, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }
    public bool EmailNotifications { get => _emailNotifications; set => SetProperty(ref _emailNotifications, value); }
    public bool PasswordChanged    { get => _passwordChanged;    private set => SetProperty(ref _passwordChanged, value); }

    public List<string> ThemeOptions    { get; } = new() { "Light", "Dark" };
    public List<string> LanguageOptions { get; } = new() { "sv-SE", "en-US" };

    public UserProfileViewModel(ApiClient api, TokenStore tokenStore) : base(api, tokenStore)
    {
        var user = tokenStore.CurrentUser;
        if (user is null) return;
        FullName = user.FullName ?? string.Empty;
        Email    = user.Email;
        UserName = user.UserName;
        Role     = user.Roles.FirstOrDefault() ?? "User";
        Theme    = user.Preferences?.Theme    ?? "Light";
        Language = user.Preferences?.Language ?? "sv-SE";
        EmailNotifications = user.Preferences?.EmailNotifications ?? true;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        await RunSafeAsync(async () =>
        {
            await Api.PutAsync<object>("api/users/me", new { FullName, Email });
            var updated = await Api.GetAsync<UserDto>("api/users/me");
            if (updated is not null && TokenStore.CurrentUser is not null)
            {
                TokenStore.CurrentUser.FullName = updated.FullName;
                TokenStore.CurrentUser.Email    = updated.Email;
            }
        }, "Status_Saving");
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        await RunSafeAsync(async () =>
        {
            await Api.PutAsync<object>("api/users/me/preferences",
                new UserPreferencesDto { Theme = Theme, Language = Language, EmailNotifications = EmailNotifications });
            LocalizationService.Current.CurrentLanguage = Language;
        }, "Status_Saving");
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        if (NewPassword != ConfirmPassword) { SetError("Lösenorden matchar inte."); return; }
        if (string.IsNullOrWhiteSpace(CurrentPassword)) { SetError("Ange nuvarande lösenord."); return; }

        await RunSafeAsync(async () =>
        {
            await Api.PostAsync<object>("api/users/me/change-password",
                new ChangePasswordDto { CurrentPassword = CurrentPassword, NewPassword = NewPassword, ConfirmNewPassword = ConfirmPassword });
            PasswordChanged = true;
            CurrentPassword = NewPassword = ConfirmPassword = string.Empty;
        }, "Status_Saving");
    }
}
