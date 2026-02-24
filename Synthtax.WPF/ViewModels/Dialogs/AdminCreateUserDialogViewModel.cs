using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels.Dialogs;

public partial class AdminCreateUserDialogViewModel : ViewModelBase
{
    private string _fullName = string.Empty, _userName = string.Empty;
    private string _email = string.Empty, _selectedRole = "User";
    public string Password { get; set; } = string.Empty;

    public string FullName     { get => _fullName;      set => SetProperty(ref _fullName, value); }
    public string UserName     { get => _userName;      set => SetProperty(ref _userName, value); }
    public string Email        { get => _email;         set => SetProperty(ref _email, value); }
    public string SelectedRole { get => _selectedRole;  set => SetProperty(ref _selectedRole, value); }

    public List<string> RoleOptions { get; } = new() { "User", "Admin" };

    public event EventHandler<bool>? DialogClosed;

    public AdminCreateUserDialogViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName))  { SetError("Användarnamn krävs.");    return; }
        if (string.IsNullOrWhiteSpace(Email))      { SetError("E-postadress krävs.");    return; }
        if (string.IsNullOrWhiteSpace(Password))   { SetError("Lösenord krävs.");        return; }

        await RunSafeAsync(async () =>
        {
            var result = await Api.PostAsync<UserDto>("api/admin/users", new AdminCreateUserDto
            {
                FullName = FullName,
                UserName = UserName,
                Email    = Email,
                Password = Password,
                Role     = SelectedRole,
            });

            if (result is not null)
                DialogClosed?.Invoke(this, true);
            else
                SetError("Kunde inte skapa användaren. Kontrollera att användarnamnet och e-postadressen är unika.");
        }, "Status_Saving");
    }
}
