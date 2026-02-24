using System.Collections.ObjectModel;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public class NavItem : ObservableObject
{
    public string Key { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool IsAdminOnly { get; init; }

    private string _label = string.Empty;
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public partial class MainViewModel : ObservableObject
{
    private readonly NavigationService _nav;
    private readonly TokenStore _tokenStore;
    private readonly LocalizationService _l = LocalizationService.Current;
    private IServiceProvider? _services;

    private string _currentUserName = string.Empty;
    private string _currentUserRole = string.Empty;
    private string _backlogStatus = string.Empty;
    private string _globalStatus = string.Empty;
    private string _currentModuleLabel = string.Empty;

    public ObservableCollection<NavItem> NavItems { get; } = new();
    public UserControl? CurrentView => _nav.CurrentView;

    public string CurrentUserName   { get => _currentUserName;   private set => SetProperty(ref _currentUserName, value); }
    public string CurrentUserRole   { get => _currentUserRole;   private set => SetProperty(ref _currentUserRole, value); }
    public string BacklogStatus     { get => _backlogStatus;     private set => SetProperty(ref _backlogStatus, value); }
    public string GlobalStatus      { get => _globalStatus;      set => SetProperty(ref _globalStatus, value); }
    public string CurrentModuleLabel{ get => _currentModuleLabel;private set => SetProperty(ref _currentModuleLabel, value); }
    public bool IsAdmin => _tokenStore.IsAdmin;

    public MainViewModel(NavigationService nav, TokenStore tokenStore)
    {
        _nav = nav;
        _tokenStore = tokenStore;
        _nav.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CurrentView));
    }

    public void Initialize(IServiceProvider services)
    {
        _services = services;
        BuildNavItems();
        InitializeUser();
        if (NavItems.Any())
            SelectNavItemCommand.Execute(NavItems.First());
    }

    private void BuildNavItems()
    {
        NavItems.Clear();
        var modules = new[]
        {
            new NavItem { Key = "CodeAnalysis",    Icon = "🔍" },
            new NavItem { Key = "Metrics",         Icon = "📊" },
            new NavItem { Key = "Git",             Icon = "🌿" },
            new NavItem { Key = "Security",        Icon = "🔒" },
            new NavItem { Key = "Backlog",         Icon = "📋" },
            new NavItem { Key = "Structure",       Icon = "🏗"  },
            new NavItem { Key = "PullRequests",    Icon = "🔀" },
            new NavItem { Key = "MethodExplorer",  Icon = "⚙"  },
            new NavItem { Key = "CommentExplorer", Icon = "💬" },
            new NavItem { Key = "AIDetection",     Icon = "🤖" },
            new NavItem { Key = "UserProfile",     Icon = "👤" },
            new NavItem { Key = "Admin",           Icon = "🛡",  IsAdminOnly = true },
        };
        foreach (var item in modules)
        {
            if (item.IsAdminOnly && !_tokenStore.IsAdmin) continue;
            var user = _tokenStore.CurrentUser;
            if (user?.AllowedModules is { Count: > 0 } allowed && !allowed.Contains(item.Key) && !item.IsAdminOnly) continue;
            item.Label = _l[$"Nav_{item.Key}"];
            NavItems.Add(item);
        }
    }

    private void InitializeUser()
    {
        var user = _tokenStore.CurrentUser;
        if (user is null) return;
        CurrentUserName = user.FullName ?? user.UserName;
        CurrentUserRole = user.Roles.FirstOrDefault() ?? "User";
        GlobalStatus = _l["Status_Ready"];
        _ = LoadBacklogStatusAsync();
    }

    private async Task LoadBacklogStatusAsync()
    {
        if (_services is null) return;
        var api = _services.GetRequiredService<ApiClient>();
        var s = await api.GetAsync<Synthtax.Core.DTOs.BacklogSummaryDto>("api/backlog/summary");
        if (s is not null)
            BacklogStatus = $"Backlog: {s.TodoCount} att göra · {s.HighPriorityCount} hög prio";
    }

    [RelayCommand]
    private void SelectNavItem(NavItem item)
    {
        if (_services is null) return;
        foreach (var n in NavItems) n.IsSelected = false;
        item.IsSelected = true;
        CurrentModuleLabel = item.Label;
        UserControl view = item.Key switch
        {
            "CodeAnalysis"    => _services.GetRequiredService<Views.CodeAnalysisView>(),
            "Metrics"         => _services.GetRequiredService<Views.MetricsView>(),
            "Git"             => _services.GetRequiredService<Views.GitView>(),
            "Security"        => _services.GetRequiredService<Views.SecurityView>(),
            "Backlog"         => _services.GetRequiredService<Views.BacklogView>(),
            "Structure"       => _services.GetRequiredService<Views.StructureAnalysisView>(),
            "PullRequests"    => _services.GetRequiredService<Views.PullRequestsView>(),
            "MethodExplorer"  => _services.GetRequiredService<Views.MethodExplorerView>(),
            "CommentExplorer" => _services.GetRequiredService<Views.CommentExplorerView>(),
            "AIDetection"     => _services.GetRequiredService<Views.AIDetectionView>(),
            "UserProfile"     => _services.GetRequiredService<Views.UserProfileView>(),
            "Admin"           => _services.GetRequiredService<Views.AdminView>(),
            _                 => _services.GetRequiredService<Views.CodeAnalysisView>()
        };
        _nav.NavigateTo(view, item.Key);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        if (_services is null) return;
        await _services.GetRequiredService<ApiClient>().LogoutAsync();
        (System.Windows.Application.Current as App)?.ShowLogin();
    }

    [RelayCommand]
    private void SwitchLanguage(string language)
    {
        LocalizationService.Current.CurrentLanguage = language;
        foreach (var item in NavItems)
            item.Label = LocalizationService.Current[$"Nav_{item.Key}"];
        if (NavItems.FirstOrDefault(n => n.IsSelected) is { } sel)
            CurrentModuleLabel = sel.Label;
    }
}
