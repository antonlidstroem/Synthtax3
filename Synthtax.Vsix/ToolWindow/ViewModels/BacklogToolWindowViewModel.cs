using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Analyzers;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.CodeFixes;
using Synthtax.Vsix.Package;

namespace Synthtax.Vsix.ToolWindow.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════
// BacklogItemViewModel  — rad i listan
// ═══════════════════════════════════════════════════════════════════════════

public sealed partial class BacklogItemViewModel : ObservableObject
{
    public Guid   Id          { get; }
    public string RuleId      { get; }
    public string Severity    { get; }
    public string Status      { get; }
    public string FilePath    { get; }
    public string FileName    => System.IO.Path.GetFileName(FilePath);
    public int    StartLine   { get; }
    public string Message     { get; }
    public string? ClassName  { get; }
    public string? MemberName { get; }
    public string Scope       => $"{ClassName ?? "?"}.{MemberName ?? "?"}";
    public string DisplayText => $"[{RuleId}] {Message}";
    public bool   IsAutoFixable { get; }

    // Bakgrundsfärg sätts av SeverityToColorConverter i XAML
    public string SeverityLevel => Severity;

    // ── Konstruktor från DTO ───────────────────────────────────────────────

    public BacklogItemViewModel(BacklogItemDto dto)
    {
        Id          = dto.Id;
        RuleId      = dto.RuleId;
        Severity    = dto.Severity;
        Status      = dto.Status;
        FilePath    = dto.FilePath;
        StartLine   = dto.StartLine;
        Message     = dto.Message;
        ClassName   = dto.ClassName;
        MemberName  = dto.MemberName;
        IsAutoFixable = dto.IsAutoFixable;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// BacklogToolWindowViewModel  — huvud ViewModel
// ═══════════════════════════════════════════════════════════════════════════

public sealed partial class BacklogToolWindowViewModel : ObservableObject
{
    private readonly SynthtaxApiClient _api;
    private readonly SynthtaxPackage   _package;

    private ObservableCollection<BacklogItemViewModel> _allItems = [];

    // ── Bindningsbara properties ──────────────────────────────────────────

    [ObservableProperty] private string  _statusText   = "Klicka Refresh för att ladda issues.";
    [ObservableProperty] private bool    _isLoading    = false;
    [ObservableProperty] private bool    _isLoggedIn   = false;
    [ObservableProperty] private string  _errorMessage = "";
    [ObservableProperty] private bool    _hasError     = false;

    // Projekthälsa
    [ObservableProperty] private string  _projectName      = "—";
    [ObservableProperty] private double  _healthScore      = 0;
    [ObservableProperty] private int     _totalIssues      = 0;
    [ObservableProperty] private int     _criticalCount    = 0;
    [ObservableProperty] private int     _highCount        = 0;
    [ObservableProperty] private string  _healthScoreText  = "—";
    [ObservableProperty] private string  _subscriptionPlan = "Free";

    // Filter
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private string _filterSeverity = "All";

    // CollectionView för sökning och filtrering
    public ICollectionView FilteredItems { get; }

    [ObservableProperty] private BacklogItemViewModel? _selectedItem;

    // Paginering
    private int _currentPage = 1;
    [ObservableProperty] private bool _hasNextPage   = false;
    [ObservableProperty] private bool _hasPrevPage   = false;
    [ObservableProperty] private string _pageText    = "";

    public BacklogToolWindowViewModel(SynthtaxPackage package)
    {
        _package = package;
        _api     = package.ApiClient;
        IsLoggedIn = package.AuthTokenService.IsAuthenticated;

        FilteredItems = CollectionViewSource.GetDefaultView(_allItems);
        FilteredItems.Filter = FilterItem;

        // Reagera på filterchandringar
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilterText) or nameof(FilterSeverity))
                FilteredItems.Refresh();
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Kommandon
    // ═══════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsLoggedIn)
        {
            await LoginAsync();
            if (!IsLoggedIn) return;
        }

        IsLoading   = true;
        HasError    = false;
        StatusText  = "Hämtar issues...";

        try
        {
            // Hämta hälsa och backlog parallellt
            var healthTask  = _api.GetProjectHealthAsync();
            var backlogTask = _api.GetBacklogAsync(
                page: _currentPage,
                severity: FilterSeverity == "All" ? null : FilterSeverity);

            await Task.WhenAll(healthTask, backlogTask);

            var health  = await healthTask;
            var backlog = await backlogTask;

            // Uppdatera hälsodata
            ProjectName      = health.ProjectName;
            HealthScore      = health.OverallScore;
            TotalIssues      = health.TotalIssues;
            CriticalCount    = health.CriticalCount;
            HighCount        = health.HighCount;
            HealthScoreText  = $"{health.OverallScore:F0}/100";
            SubscriptionPlan = health.SubscriptionPlan;

            // Uppdatera itemlistan
            _allItems.Clear();
            foreach (var dto in backlog.Items)
                _allItems.Add(new BacklogItemViewModel(dto));

            // Uppdatera extern cache för CodeFix och DiagnosticProvider
            BacklogItemCache.Update(backlog.Items);
            SynthtaxDiagnosticProvider.UpdateCache(backlog.Items);

            // Paginering
            HasNextPage = _currentPage < backlog.TotalPages;
            HasPrevPage = _currentPage > 1;
            PageText    = $"Sida {_currentPage} av {backlog.TotalPages} ({backlog.TotalCount} issues)";

            StatusText  = $"Uppdaterad {DateTime.Now:HH:mm:ss} — {backlog.TotalCount} issues";
            IsLoading   = false;
        }
        catch (UnauthorizedException ex)
        {
            IsLoggedIn    = false;
            ErrorMessage  = ex.Message;
            HasError      = true;
            StatusText    = "Ej inloggad.";
            IsLoading     = false;
        }
        catch (LicenseException ex)
        {
            ErrorMessage  = $"Licensgräns: {ex.Message}";
            HasError      = true;
            StatusText    = "Licensgräns nådd.";
            IsLoading     = false;
        }
        catch (Exception ex)
        {
            ErrorMessage  = $"Fel: {ex.Message}";
            HasError      = true;
            StatusText    = "Anslutningsfel.";
            IsLoading     = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var window = System.Windows.Application.Current?.MainWindow;
        var result = await Auth.LoginDialog.ShowAsync(_api, _package.AuthTokenService, window);
        IsLoggedIn = result;

        if (result)
            await RefreshAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!HasNextPage) return;
        _currentPage++;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task PrevPageAsync()
    {
        if (!HasPrevPage) return;
        _currentPage--;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenInEditorAsync(BacklogItemViewModel? item)
    {
        if (item is null) return;
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        try
        {
            var dte = (EnvDTE.DTE?)Microsoft.VisualStudio.Shell.Package
                .GetGlobalService(typeof(EnvDTE.DTE));

            // Hitta absolut sökväg via solution-kontext
            var solution = dte?.Solution;
            if (solution is null) return;

            var solutionDir = System.IO.Path.GetDirectoryName(solution.FileName) ?? "";
            var absPath     = System.IO.Path.Combine(solutionDir, item.FilePath.Replace('/', '\\'));

            if (!System.IO.File.Exists(absPath)) return;

            dte!.ItemOperations.OpenFile(absPath);

            // Navigera till rätt rad
            var doc = dte.ActiveDocument;
            if (doc?.Selection is EnvDTE.TextSelection sel)
                sel.GotoLine(item.StartLine, Select: false);
        }
        catch { /* Tyst — filen kanske inte finns lokalt */ }
    }

    // HasFilterText — styr synlighet för ✕-knapp
    public bool HasFilterText => !string.IsNullOrEmpty(FilterText);
    partial void OnFilterTextChanged(string value) => OnPropertyChanged(nameof(HasFilterText));

    [RelayCommand]
    private void ClearSearch()
    {
        FilterText      = "";
        FilterSeverity  = "All";
    }

    [RelayCommand]
    private async Task FixWithCopilotAsync(BacklogItemViewModel? item)
    {
        if (item is null) return;
        var promptText = await FetchPromptAsync(item.Dto, "Copilot");
        await _package.PromptDispatch.SendToCopilotAsync(promptText);
    }

    [RelayCommand]
    private async Task ExportForClaudeAsync(BacklogItemViewModel? item)
    {
        if (item is null) return;
        var promptText = await FetchPromptAsync(item.Dto, "Claude");
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        System.Windows.Clipboard.SetText(promptText);
        StatusText = "✅ Claude-prompt kopierad till urklipp.";
    }

    private async Task<string> FetchPromptAsync(BacklogItemDto dto, string target)
    {
        try
        {
            var resp = await _api.GetPromptAsync(dto.Id, target);
            return resp.Content;
        }
        catch
        {
            // Fallback: bygg prompt lokalt om API inte svarar
            return target == "Copilot"
                ? $"// Synthtax [{dto.RuleId}] {dto.Severity}\n// {dto.Message}\n// File: {dto.FilePath}:{dto.StartLine}\n// Fix: {dto.Suggestion}"
                : $"# Synthtax [{dto.RuleId}]\n**{dto.Message}**\nFile: `{dto.FilePath}:{dto.StartLine}`\n\n```csharp\n{dto.Snippet}\n```\n\n{dto.Suggestion}";
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Filtrering
    // ═══════════════════════════════════════════════════════════════════════

    private bool FilterItem(object obj)
    {
        if (obj is not BacklogItemViewModel vm) return false;

        // Severity-filter
        if (FilterSeverity is not ("All" or ""))
            if (!string.Equals(vm.Severity, FilterSeverity, StringComparison.OrdinalIgnoreCase))
                return false;

        // Text-sökning (RuleId, Message, ClassName, FilePath)
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var q = FilterText.Trim();
            return vm.RuleId.Contains(q, StringComparison.OrdinalIgnoreCase)
                || vm.Message.Contains(q, StringComparison.OrdinalIgnoreCase)
                || vm.FilePath.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (vm.ClassName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
        }

        return true;
    }
}
