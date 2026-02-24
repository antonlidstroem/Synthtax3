using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;
using Synthtax.WPF.ViewModels.Dialogs;
using Synthtax.WPF.Views.Dialogs;

namespace Synthtax.WPF.ViewModels;

public partial class AdminViewModel : ViewModelBase
{
    private bool   _showUsers    = true;
    private bool   _showAuditLog = false;
    private string _userSearch   = string.Empty;
    private string _auditSearch  = string.Empty;
    private UserDto? _selectedUser;
    private int _auditPage = 1;

    public bool ShowUsers    { get => _showUsers;    set { if (SetProperty(ref _showUsers, value)    && value) _ = LoadUsersAsync(); } }
    public bool ShowAuditLog { get => _showAuditLog; set { if (SetProperty(ref _showAuditLog, value) && value) _ = LoadAuditAsync(reset: true); } }

    public string UserSearch
    {
        get => _userSearch;
        set { if (SetProperty(ref _userSearch, value)) FilterUsers(); }
    }
    public string AuditSearch
    {
        get => _auditSearch;
        set { if (SetProperty(ref _auditSearch, value)) FilterAudit(); }
    }

    public UserDto? SelectedUser { get => _selectedUser; set => SetProperty(ref _selectedUser, value); }

    public ObservableCollection<UserDto>     Users     { get; } = new();
    public ObservableCollection<AuditLogDto> AuditLogs { get; } = new();

    private List<UserDto>     _allUsers     = new();
    private List<AuditLogDto> _allAuditLogs = new();

    public AdminViewModel(ApiClient api, TokenStore tokenStore) : base(api, tokenStore)
    {
        _ = LoadUsersAsync();
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    private async Task LoadUsersAsync()
    {
        await RunSafeAsync(async () =>
        {
            var result = await Api.GetAsync<List<UserDto>>("api/admin/users");
            _allUsers = result ?? new List<UserDto>();
            FilterUsers();
        });
    }

    private async Task LoadAuditAsync(bool reset = false)
    {
        if (reset) { _auditPage = 1; _allAuditLogs.Clear(); }

        var result = await Api.GetAsync<PagedResultDto<AuditLogDto>>(
            $"api/admin/audit-log?page={_auditPage}&pageSize=100");

        if (result?.Items is not null)
            _allAuditLogs.AddRange(result.Items);

        FilterAudit();
    }

    private void FilterUsers()
    {
        Users.Clear();
        foreach (var u in _allUsers.Where(u =>
            string.IsNullOrWhiteSpace(_userSearch)
            || u.UserName.Contains(_userSearch, StringComparison.OrdinalIgnoreCase)
            || (u.FullName?.Contains(_userSearch, StringComparison.OrdinalIgnoreCase) ?? false)
            || u.Email.Contains(_userSearch, StringComparison.OrdinalIgnoreCase)))
            Users.Add(u);
    }

    private void FilterAudit()
    {
        AuditLogs.Clear();
        foreach (var a in _allAuditLogs.Where(a =>
            string.IsNullOrWhiteSpace(_auditSearch)
            || a.Action.Contains(_auditSearch, StringComparison.OrdinalIgnoreCase)
            || (a.ResourceType?.Contains(_auditSearch, StringComparison.OrdinalIgnoreCase) ?? false)
            || (a.Details?.Contains(_auditSearch, StringComparison.OrdinalIgnoreCase) ?? false)))
            AuditLogs.Add(a);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CreateUser()
    {
        var owner = Application.Current.MainWindow;
        var vm    = new AdminCreateUserDialogViewModel(Api, TokenStore);
        var dlg   = new AdminCreateUserDialog(vm, owner);
        if (dlg.ShowDialog() == true)
            _ = LoadUsersAsync();
    }

    [RelayCommand]
    private async Task SetActiveAsync(string param)
    {
        if (SelectedUser is null) return;
        var isActive = param == "True";
        var action   = isActive ? "aktivera" : "inaktivera";

        var owner   = Application.Current.MainWindow;
        var confirm = new ConfirmDialog(
            isActive ? "Aktivera konto" : "Inaktivera konto",
            $"Vill du {action} kontot för '{SelectedUser.UserName}'?",
            isActive ? "Aktivera" : "Inaktivera", "Avbryt",
            isDangerous: !isActive, owner);

        if (confirm.ShowDialog() != true) return;

        await RunSafeAsync(async () =>
        {
            await Api.PatchAsync<object>(
                $"api/admin/users/{SelectedUser.Id}/active",
                new { IsActive = isActive });
            await LoadUsersAsync();
        });
    }

    [RelayCommand]
    private void ResetPassword()
    {
        if (SelectedUser is null) return;
        var owner = Application.Current.MainWindow;
        var vm    = new AdminResetPasswordDialogViewModel(Api, TokenStore, SelectedUser.Id, SelectedUser.UserName);
        var dlg   = new AdminResetPasswordDialog(vm, owner);
        dlg.ShowDialog();
    }

    [RelayCommand]
    private void EditModules()
    {
        if (SelectedUser is null) return;
        var owner   = Application.Current.MainWindow;
        var current = SelectedUser.AllowedModules ?? new List<string>();
        var vm      = new AdminModulesDialogViewModel(Api, TokenStore, SelectedUser.Id, SelectedUser.UserName, current);
        var dlg     = new AdminModulesDialog(vm, owner);
        if (dlg.ShowDialog() == true)
            _ = LoadUsersAsync();
    }

    [RelayCommand]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser is null) return;
        var owner   = Application.Current.MainWindow;
        var confirm = new ConfirmDialog(
            "Ta bort användare",
            $"Är du säker på att du vill ta bort '{SelectedUser.UserName}'? Alla data för användaren raderas permanent.",
            "Ta bort", "Avbryt", isDangerous: true, owner);

        if (confirm.ShowDialog() != true) return;

        await RunSafeAsync(async () =>
        {
            await Api.DeleteAsync($"api/admin/users/{SelectedUser.Id}");
            await LoadUsersAsync();
        });
    }

    [RelayCommand]
    private async Task LoadMoreAuditAsync()
    {
        _auditPage++;
        await LoadAuditAsync();
    }
}
