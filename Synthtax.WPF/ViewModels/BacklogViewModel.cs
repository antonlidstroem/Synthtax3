using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.WPF.Services;
using Synthtax.WPF.ViewModels.Dialogs;
using Synthtax.WPF.Views.Dialogs;
using System.IO;

namespace Synthtax.WPF.ViewModels;

public class FilterOption<T> where T : struct
{
    public string Label { get; set; } = string.Empty;
    public T? Value { get; set; }
}

public partial class BacklogViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;
    private string _searchText = string.Empty;
    private FilterOption<BacklogStatus>? _filterStatus;
    private FilterOption<Priority>? _filterPriority;
    private FilterOption<BacklogCategory>? _filterCategory;
    private bool _myItemsOnly;
    private BacklogItemDto? _selectedItem;

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) _ = LoadAsync(); }
    }
    public FilterOption<BacklogStatus>? FilterStatus
    {
        get => _filterStatus;
        set { if (SetProperty(ref _filterStatus, value)) _ = LoadAsync(); }
    }
    public FilterOption<Priority>? FilterPriority
    {
        get => _filterPriority;
        set { if (SetProperty(ref _filterPriority, value)) _ = LoadAsync(); }
    }
    public FilterOption<BacklogCategory>? FilterCategory
    {
        get => _filterCategory;
        set { if (SetProperty(ref _filterCategory, value)) _ = LoadAsync(); }
    }
    public bool MyItemsOnly
    {
        get => _myItemsOnly;
        set { if (SetProperty(ref _myItemsOnly, value)) _ = LoadAsync(); }
    }
    public BacklogItemDto? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    public ObservableCollection<BacklogItemDto> Items { get; } = new();

    public List<FilterOption<BacklogStatus>> StatusOptions { get; } = new()
    {
        new() { Label = "Alla",       Value = null },
        new() { Label = "Att göra",   Value = BacklogStatus.Todo },
        new() { Label = "Pågår",      Value = BacklogStatus.InProgress },
        new() { Label = "Klar",       Value = BacklogStatus.Done },
        new() { Label = "Avbruten",   Value = BacklogStatus.Cancelled },
    };
    public List<FilterOption<Priority>> PriorityOptions { get; } = new()
    {
        new() { Label = "Alla",  Value = null },
        new() { Label = "Låg",   Value = Priority.Low },
        new() { Label = "Medel", Value = Priority.Medium },
        new() { Label = "Hög",   Value = Priority.High },
    };
    public List<FilterOption<BacklogCategory>> CategoryOptions { get; } = new()
    {
        new() { Label = "Alla",          Value = null },
        new() { Label = "Bugg",          Value = BacklogCategory.Bug },
        new() { Label = "Refactor",      Value = BacklogCategory.Refactor },
        new() { Label = "Säkerhet",      Value = BacklogCategory.Security },
        new() { Label = "Dokumentation", Value = BacklogCategory.Documentation },
        new() { Label = "Funktion",      Value = BacklogCategory.Feature },
        new() { Label = "Prestanda",     Value = BacklogCategory.Performance },
    };

    public BacklogViewModel(ApiClient api, TokenStore tokenStore, IServiceProvider services)
        : base(api, tokenStore)
    {
        _services = services;
        _filterStatus   = StatusOptions[0];
        _filterPriority = PriorityOptions[0];
        _filterCategory = CategoryOptions[0];
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var qs = $"api/backlog?page=1&pageSize=200&myItemsOnly={_myItemsOnly}";
        if (_filterStatus?.Value is { } s)   qs += $"&status={s}";
        if (_filterPriority?.Value is { } p) qs += $"&priority={p}";
        if (_filterCategory?.Value is { } c) qs += $"&category={c}";

        var result = await Api.GetAsync<PagedResultDto<BacklogItemDto>>(qs);
        Items.Clear();
        if (result is null) return;

        foreach (var item in result.Items.Where(i =>
            string.IsNullOrWhiteSpace(_searchText)
            || i.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)))
            Items.Add(item);
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _searchText = string.Empty; OnPropertyChanged(nameof(SearchText));
        FilterStatus   = StatusOptions[0];
        FilterPriority = PriorityOptions[0];
        FilterCategory = CategoryOptions[0];
    }

    [RelayCommand]
    private void CreateItem()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        var vm    = new BacklogItemDialogViewModel(Api, TokenStore);
        var dlg   = new BacklogItemDialog(vm, owner);
        if (dlg.ShowDialog() == true)
            _ = LoadAsync();
    }

    [RelayCommand]
    private void EditItem(BacklogItemDto? item)
    {
        if (item is null) return;
        var owner = System.Windows.Application.Current.MainWindow;
        var vm    = new BacklogItemDialogViewModel(Api, TokenStore, item);
        var dlg   = new BacklogItemDialog(vm, owner);
        if (dlg.ShowDialog() == true)
            _ = LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteItemAsync(BacklogItemDto? item)
    {
        if (item is null) return;
        var owner = System.Windows.Application.Current.MainWindow;
        var confirm = new ConfirmDialog(
            "Ta bort ärende",
            $"Är du säker på att du vill ta bort '{item.Title}'? Åtgärden kan inte ångras.",
            "Ta bort", "Avbryt", isDangerous: true, owner);

        if (confirm.ShowDialog() != true) return;

        await RunSafeAsync(async () =>
        {
            await Api.DeleteAsync($"api/backlog/{item.Id}");
            await LoadAsync();
        });
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var (bytes, _, fn) = await Api.DownloadAsync("api/backlog/export/csv");
        if (bytes is not null)
        {
            var d = new SaveFileDialog { FileName = fn ?? "Backlog.csv", Filter = "CSV|*.csv" };
            if (d.ShowDialog() == true) File.WriteAllBytes(d.FileName, bytes);
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        var (bytes, _, fn) = await Api.DownloadAsync("api/backlog/export/json");
        if (bytes is not null)
        {
            var d = new SaveFileDialog { FileName = fn ?? "Backlog.json", Filter = "JSON|*.json" };
            if (d.ShowDialog() == true) File.WriteAllBytes(d.FileName, bytes);
        }
    }
}
