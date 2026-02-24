using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels.Dialogs;

public class EnumOption<T>
{
    public string Label { get; set; } = string.Empty;
    public T Value { get; set; } = default!;
}

public partial class BacklogItemDialogViewModel : ViewModelBase
{
    private readonly BacklogItemDto? _editItem;

    private string _title       = string.Empty;
    private string _description = string.Empty;
    private string _linkedFilePath = string.Empty;
    private string _tags        = string.Empty;
    private DateTime? _deadline;

    private EnumOption<BacklogStatus>   _selectedStatus   = default!;
    private EnumOption<Priority>        _selectedPriority = default!;
    private EnumOption<BacklogCategory> _selectedCategory = default!;

    public string DialogTitle    => _editItem is null ? "Nytt ärende" : "Redigera ärende";
    public string SaveButtonLabel => _editItem is null ? "Skapa ärende" : "Spara ändringar";

    public string  Title          { get => _title;         set => SetProperty(ref _title, value); }
    public string  Description    { get => _description;   set => SetProperty(ref _description, value); }
    public string  LinkedFilePath { get => _linkedFilePath;set => SetProperty(ref _linkedFilePath, value); }
    public string  Tags           { get => _tags;          set => SetProperty(ref _tags, value); }
    public DateTime? Deadline     { get => _deadline;      set => SetProperty(ref _deadline, value); }

    public EnumOption<BacklogStatus>   SelectedStatus   { get => _selectedStatus;   set => SetProperty(ref _selectedStatus, value); }
    public EnumOption<Priority>        SelectedPriority { get => _selectedPriority; set => SetProperty(ref _selectedPriority, value); }
    public EnumOption<BacklogCategory> SelectedCategory { get => _selectedCategory; set => SetProperty(ref _selectedCategory, value); }

    public List<EnumOption<BacklogStatus>> StatusOptions { get; } = new()
    {
        new() { Label = "Att göra",  Value = BacklogStatus.Todo },
        new() { Label = "Pågår",     Value = BacklogStatus.InProgress },
        new() { Label = "Klar",      Value = BacklogStatus.Done },
        new() { Label = "Avbruten",  Value = BacklogStatus.Cancelled },
    };

    public List<EnumOption<Priority>> PriorityOptions { get; } = new()
    {
        new() { Label = "Låg",   Value = Priority.Low },
        new() { Label = "Medel", Value = Priority.Medium },
        new() { Label = "Hög",   Value = Priority.High },
    };

    public List<EnumOption<BacklogCategory>> CategoryOptions { get; } = new()
    {
        new() { Label = "Bugg",          Value = BacklogCategory.Bug },
        new() { Label = "Refactor",      Value = BacklogCategory.Refactor },
        new() { Label = "Dokumentation", Value = BacklogCategory.Documentation },
        new() { Label = "Säkerhet",      Value = BacklogCategory.Security },
        new() { Label = "Funktion",      Value = BacklogCategory.Feature },
        new() { Label = "Prestanda",     Value = BacklogCategory.Performance },
    };

    public event EventHandler<bool>? DialogClosed;

    // ── Constructor (new item) ───────────────────────────────────────────
    public BacklogItemDialogViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore)
    {
        _selectedStatus   = StatusOptions[0];
        _selectedPriority = PriorityOptions[1]; // Medium default
        _selectedCategory = CategoryOptions[0];
    }

    // ── Constructor (edit existing) ──────────────────────────────────────
    public BacklogItemDialogViewModel(
        ApiClient api, TokenStore tokenStore, BacklogItemDto existing)
        : base(api, tokenStore)
    {
        _editItem = existing;
        Title          = existing.Title;
        Description    = existing.Description ?? string.Empty;
        LinkedFilePath = existing.LinkedFilePath ?? string.Empty;
        Tags           = existing.Tags ?? string.Empty;
        Deadline       = existing.Deadline;

        _selectedStatus   = StatusOptions.FirstOrDefault(o => o.Value == existing.Status)   ?? StatusOptions[0];
        _selectedPriority = PriorityOptions.FirstOrDefault(o => o.Value == existing.Priority) ?? PriorityOptions[1];
        _selectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == existing.Category) ?? CategoryOptions[0];
    }

    [RelayCommand]
    private void BrowseFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Välj kopplad fil",
            Filter = "C# files (*.cs)|*.cs|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            LinkedFilePath = dlg.FileName;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            SetError("Titel är obligatorisk.");
            return;
        }

        await RunSafeAsync(async () =>
        {
            if (_editItem is null)
            {
                // Create
                await Api.PostAsync<BacklogItemDto>("api/backlog", new CreateBacklogItemDto
                {
                    Title          = Title,
                    Description    = Description,
                    Status         = SelectedStatus.Value,
                    Priority       = SelectedPriority.Value,
                    Category       = SelectedCategory.Value,
                    Deadline       = Deadline,
                    LinkedFilePath = LinkedFilePath,
                    Tags           = Tags,
                });
            }
            else
            {
                // Update
                await Api.PutAsync<BacklogItemDto>($"api/backlog/{_editItem.Id}", new UpdateBacklogItemDto
                {
                    Title          = Title,
                    Description    = Description,
                    Status         = SelectedStatus.Value,
                    Priority       = SelectedPriority.Value,
                    Category       = SelectedCategory.Value,
                    Deadline       = Deadline,
                    LinkedFilePath = LinkedFilePath,
                    Tags           = Tags,
                });
            }

            DialogClosed?.Invoke(this, true);
        }, "Status_Saving");
    }
}
