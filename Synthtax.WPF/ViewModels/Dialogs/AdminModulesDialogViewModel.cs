using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels.Dialogs;

public class ModuleItem : ObservableObject
{
    public string Key         { get; init; } = string.Empty;
    public string Icon        { get; init; } = string.Empty;
    public string Label       { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

public partial class AdminModulesDialogViewModel : ViewModelBase
{
    private readonly string _userId;
    private readonly string _userName;

    public string SubTitle => $"Användare: {_userName}";

    public ObservableCollection<ModuleItem> ModuleItems { get; } = new();

    public event EventHandler<bool>? DialogClosed;

    private static readonly (string Key, string Icon, string Label, string Desc)[] AllModules =
    {
        ("CodeAnalysis",    "🔍", "Kodanalys",              "Roslyn-baserad analys av kodkvalitet"),
        ("Metrics",         "📊", "Metrics",                "LOC, komplexitet och underhållsindex"),
        ("Git",             "🌿", "Git-analys",             "Commits, brancher, churn och bus factor"),
        ("Security",        "🔒", "Säkerhetsanalys",        "Credentials, SQL-injection och mer"),
        ("Backlog",         "📋", "Backlog",                "Tekniska ärenden och uppgifter"),
        ("Structure",       "🏗",  "Strukturanalys",         "Trädvy över solution-struktur"),
        ("PullRequests",    "🔀", "Pull Requests",          "PR-integration (utökningsbar)"),
        ("MethodExplorer",  "⚙",  "Metodutforskaren",       "Sök och filtrera metoder"),
        ("CommentExplorer", "💬", "Kommentarsutforskaren",  "TODO, kommentarer och regioner"),
        ("AIDetection",     "🤖", "AI-detektering",         "Heuristisk analys av AI-kod"),
    };

    public AdminModulesDialogViewModel(
        ApiClient api, TokenStore tokenStore,
        string userId, string userName, List<string> currentModules)
        : base(api, tokenStore)
    {
        _userId   = userId;
        _userName = userName;

        foreach (var (key, icon, label, desc) in AllModules)
        {
            ModuleItems.Add(new ModuleItem
            {
                Key         = key,
                Icon        = icon,
                Label       = label,
                Description = desc,
                IsEnabled   = currentModules.Count == 0 || currentModules.Contains(key)
            });
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var m in ModuleItems) m.IsEnabled = true;
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var m in ModuleItems) m.IsEnabled = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunSafeAsync(async () =>
        {
            var enabledModules = ModuleItems
                .Where(m => m.IsEnabled)
                .Select(m => m.Key)
                .ToList();

            await Api.PatchAsync<object>($"api/admin/users/{_userId}/modules",
                new { AllowedModules = enabledModules });

            DialogClosed?.Invoke(this, true);
        }, "Status_Saving");
    }
}
