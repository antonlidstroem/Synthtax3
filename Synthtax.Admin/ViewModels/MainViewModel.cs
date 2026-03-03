using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Synthtax.Admin.Models;
using Synthtax.Admin.Services;

namespace Synthtax.Admin.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AuthService _auth;
    private readonly UserService _userSvc;

    private UserModel? _selectedUser;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private string _newPassword = string.Empty;

    public ObservableCollection<UserModel> Users { get; } = [];
    public bool IsSuperAdmin => _auth.CurrentRole == "SuperAdmin";

    public UserModel? SelectedUser
    {
        get => _selectedUser;
        set { _selectedUser = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); }
    }

    public bool HasSelection => _selectedUser != null;

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string NewPassword
    {
        get => _newPassword;
        set { _newPassword = value; OnPropertyChanged(); }
    }

    public ICommand RefreshCommand       { get; }
    public ICommand DeleteUserCommand    { get; }
    public ICommand ToggleLockCommand    { get; }
    public ICommand ToggleAdminCommand   { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand LogoutCommand        { get; }

    public MainViewModel(AuthService auth, UserService userSvc)
    {
        _auth    = auth;
        _userSvc = userSvc;

        RefreshCommand       = new RelayCommand(async () => await LoadUsersAsync());
        DeleteUserCommand    = new RelayCommand(async () => await DeleteUserAsync(),    () => HasSelection);
        ToggleLockCommand    = new RelayCommand(async () => await ToggleLockAsync(),    () => HasSelection);
        ToggleAdminCommand   = new RelayCommand(async () => await ToggleAdminAsync(),   () => HasSelection && IsSuperAdmin);
        ResetPasswordCommand = new RelayCommand(async () => await ResetPasswordAsync(), () => HasSelection);
        LogoutCommand        = new RelayCommand(DoLogout);

        _ = LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        IsBusy = true;
        StatusMessage = "Laddar användare...";
        try
        {
            var users = await _userSvc.GetAllUsersAsync();
            Users.Clear();
            foreach (var u in users ?? [])
                Users.Add(u);
            StatusMessage = $"{Users.Count} användare laddade.";
        }
        catch (Exception ex) { StatusMessage = $"Fel: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task DeleteUserAsync()
    {
        if (SelectedUser is null) return;
        var confirm = MessageBox.Show(
            $"Radera {SelectedUser.UserName}?", "Bekräfta", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var resp = await _userSvc.DeleteUserAsync(SelectedUser.Id);
        if (resp.IsSuccessStatusCode)
        {
            Users.Remove(SelectedUser);
            StatusMessage = "Användaren raderades.";
        }
        else StatusMessage = $"Misslyckades: {resp.StatusCode}";
    }

    private async Task ToggleLockAsync()
    {
        if (SelectedUser is null) return;
        var locked = !SelectedUser.IsLocked;
        var resp   = await _userSvc.SetLockedAsync(SelectedUser.Id, locked);
        if (resp.IsSuccessStatusCode)
        {
            SelectedUser.IsLocked = locked;
            OnPropertyChanged(nameof(SelectedUser));
            StatusMessage = locked ? "Användaren låstes." : "Låsningen togs bort.";
            await LoadUsersAsync();
        }
        else StatusMessage = $"Misslyckades: {resp.StatusCode}";
    }

    private async Task ToggleAdminAsync()
    {
        if (SelectedUser is null) return;
        var grant = !SelectedUser.IsAdmin;
        var resp  = await _userSvc.SetRoleAsync(SelectedUser.Id, "Admin", grant);
        if (resp.IsSuccessStatusCode)
        {
            StatusMessage = grant ? "Admin-roll tilldelad." : "Admin-roll borttagen.";
            await LoadUsersAsync();
        }
        else StatusMessage = $"Misslyckades: {resp.StatusCode}";
    }

    private async Task ResetPasswordAsync()
    {
        if (SelectedUser is null || string.IsNullOrWhiteSpace(NewPassword)) return;
        var resp = await _userSvc.ResetPasswordAsync(SelectedUser.Id, NewPassword);
        if (resp.IsSuccessStatusCode)
        {
            NewPassword   = string.Empty;
            StatusMessage = "Lösenordet återställdes.";
        }
        else StatusMessage = $"Misslyckades: {resp.StatusCode}";
    }

    private void DoLogout()
    {
        _auth.Logout();
        var loginVm = new LoginViewModel(_auth);
        var login   = new Views.LoginView { DataContext = loginVm };
        login.Show();
        Application.Current.Windows[0]?.Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
