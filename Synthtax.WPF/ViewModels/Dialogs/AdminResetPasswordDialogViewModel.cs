using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels.Dialogs;

public partial class AdminResetPasswordDialogViewModel : ViewModelBase
{
    private readonly string _userId;
    private readonly string _userName;

    public string NewPassword     { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;

    public string InfoText => $"Återställ lösenord för '{_userName}'. Alla aktiva sessioner kommer att avslutas.";

    public event EventHandler<bool>? DialogClosed;

    public AdminResetPasswordDialogViewModel(
        ApiClient api, TokenStore tokenStore, string userId, string userName)
        : base(api, tokenStore)
    {
        _userId   = userId;
        _userName = userName;
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPassword))
        { SetError("Nytt lösenord krävs."); return; }

        if (NewPassword != ConfirmPassword)
        { SetError("Lösenorden matchar inte."); return; }

        await RunSafeAsync(async () =>
        {
            await Api.PostAsync<object>("api/admin/users/reset-password", new AdminResetPasswordDto
            {
                UserId      = _userId,
                NewPassword = NewPassword,
            });
            DialogClosed?.Invoke(this, true);
        }, "Status_Saving");
    }
}
