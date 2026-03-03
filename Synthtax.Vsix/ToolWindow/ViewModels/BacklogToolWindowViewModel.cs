using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Analyzers;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.CodeFixes;
using Synthtax.Vsix.Package;
using Synthtax.Vsix.Services;
using Synthtax.Vsix.SignalR;

namespace Synthtax.Vsix.ToolWindow.ViewModels;

public sealed partial class BacklogToolWindowViewModel : ObservableObject, IToolWindowRefreshTarget
{
    private readonly SynthtaxApiClient _api;
    private readonly SynthtaxPackage   _package;
    private readonly ObservableCollection<BacklogItemViewModel> _allItems = [];

    [ObservableProperty] private string _statusText      = "Klicka Refresh för att ladda issues.";
    [ObservableProperty] private bool   _isLoading       = false;
    [ObservableProperty] private bool   _isLoggedIn      = false;
    [ObservableProperty] private string _errorMessage    = "";
    [ObservableProperty] private bool   _hasError        = false;
    [ObservableProperty] private string _projectName     = "—";
    [ObservableProperty] private double _healthScore     = 0;
    [ObservableProperty] private int    _totalIssues     = 0;
    [ObservableProperty] private int    _criticalCount   = 0;
    [ObservableProperty] private int    _highCount       = 0;
    [ObservableProperty] private string _healthScoreText = "—";
    [ObservableProperty] private string _subscriptionPlan = "Free";
    [ObservableProperty] private string _filterText      = "";
    [ObservableProperty] private string _filterSeverity  = "All";

    [ObservableProperty] private string _signalRStatusText  = "";
    [ObservableProperty] private bool   _isSignalRConnected = false;

    [ObservableProperty] private BacklogItemViewModel? _selectedItem;

    // FÖRBÄTTRING #11: Räknar filtrerade items — används av ZeroToVisibleConverter
    // för att visa empty-state när filtret ger 0 träffar.
    [ObservableProperty] private int _filteredItemCount = 0;

    private int _currentPage = 1;
    [ObservableProperty] private bool   _hasNextPage = false;
    [ObservableProperty] private bool   _hasPrevPage = false;
    [ObservableProperty] private string _pageText    = "";

    public ICollectionView FilteredItems { get; }

    public BacklogToolWindowViewModel(SynthtaxPackage package)
    {
        _package   = package;
        _api       = package.ApiClient;
        IsLoggedIn = package.AuthTokenService.IsAuthenticated;

        FilteredItems = CollectionViewSource.GetDefaultView(_allItems);
        FilteredItems.Filter = FilterItem;

        // Uppdatera FilteredItemCount när filter ändras
        FilteredItems.CollectionChanged += (_, _) =>
            FilteredItemCount = FilteredItems.Cast<object>().Count();

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilterText) or nameof(FilterSeverity))
            {
                FilteredItems.Refresh();
                FilteredItemCount = FilteredItems.Cast<object>().Count();
            }
        };
    }

    // ─── IToolWindowRefreshTarget ────────────────────────────────────────────

    public async Task ApplyIncrementalUpdateAsync(
        IReadOnlyList<BacklogItemDto> added,
        IReadOnlyList<Guid>           removedIds)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var removeSet = new HashSet<Guid>(removedIds);
        for (int i = _allItems.Count - 1; i >= 0; i--)
            if (removeSet.Contains(_allItems[i].Id))
                _allItems.RemoveAt(i);

        foreach (var dto in added)
            _allItems.Insert(0, new BacklogItemViewModel(dto));

        RefreshCounts();
        FilteredItems.Refresh();
        FilteredItemCount = FilteredItems.Cast<object>().Count();
        StatusText = $"Uppdaterad {DateTime.Now:HH:mm:ss} — {TotalIssues} issues (realtid)";
    }

    public void RemoveIssue(Guid issueId)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var vm = _allItems.FirstOrDefault(i => i.Id == issueId);
        if (vm is null) return;
        _allItems.Remove(vm);
        RefreshCounts();
        FilteredItems.Refresh();
        FilteredItemCount = FilteredItems.Cast<object>().Count();
    }

    public void UpdateSubscriptionPlan(string newPlan)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SubscriptionPlan = newPlan;
    }

    internal void UpdateSignalRStatus(HubConnectionState state)
    {
        IsSignalRConnected = state == HubConnectionState.Connected;
        SignalRStatusText  = state switch
        {
            HubConnectionState.Connected    => "● Realtid",
            HubConnectionState.Connecting   => "◌ Ansluter…",
            HubConnectionState.Reconnecting => "⚠ Återsansluter",
            HubConnectionState.Disconnected => "○ Offline",
            HubConnectionState.AuthError    => "🔑 Ej inloggad",
            _                               => ""
        };
    }

    // ─── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsLoggedIn)
        {
            await LoginAsync();
            if (!IsLoggedIn) return;
        }

        IsLoading  = true;
        HasError   = false;
        StatusText = "Hämtar issues...";

        try
        {
            var healthTask  = _api.GetProjectHealthAsync();
            var backlogTask = _api.GetBacklogAsync(
                page:     _currentPage,
                severity: FilterSeverity == "All" ? null : FilterSeverity);

            await Task.WhenAll(healthTask, backlogTask);

            var health  = await healthTask;
            var backlog = await backlogTask;

            ProjectName      = health.ProjectName;
            HealthScore      = health.OverallScore;
            TotalIssues      = health.TotalIssues;
            CriticalCount    = health.CriticalCount;
            HighCount        = health.HighCount;
            HealthScoreText  = $"{health.OverallScore:F0}/100";
            SubscriptionPlan = health.SubscriptionPlan;

            _allItems.Clear();
            foreach (var dto in backlog.Items)
                _allItems.Add(new BacklogItemViewModel(dto));

            BacklogItemCache.Update(backlog.Items);
            SynthtaxDiagnosticProvider.UpdateCache(backlog.Items);

            HasNextPage = _currentPage < backlog.TotalPages;
            HasPrevPage = _currentPage > 1;
            PageText    = $"Sida {_currentPage} av {backlog.TotalPages} ({backlog.TotalCount} issues)";
            StatusText  = $"Uppdaterad {DateTime.Now:HH:mm:ss} — {backlog.TotalCount} issues";

            FilteredItems.Refresh();
            FilteredItemCount = FilteredItems.Cast<object>().Count();
        }
        catch (UnauthorizedException ex)
        {
            IsLoggedIn   = false;
            ErrorMessage = ex.Message;
            HasError     = true;
            StatusText   = "Ej inloggad.";
        }
        catch (LicenseException ex)
        {
            ErrorMessage = $"Licensgräns: {ex.Message}";
            HasError     = true;
            StatusText   = "Licensgräns nådd.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand] private async Task LoginAsync() { /* hanteras av package */ }

    // ─── Hjälpare ─────────────────────────────────────────────────────────────

    private bool FilterItem(object obj)
    {
        if (obj is not BacklogItemViewModel vm) return false;
        if (FilterSeverity != "All" && vm.Severity != FilterSeverity) return false;
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var t = FilterText.Trim();
            return vm.Message.Contains(t, StringComparison.OrdinalIgnoreCase)
                || vm.RuleId.Contains(t, StringComparison.OrdinalIgnoreCase)
                || vm.FilePath.Contains(t, StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    private void RefreshCounts()
    {
        TotalIssues   = _allItems.Count;
        CriticalCount = _allItems.Count(i => i.Severity == "Critical");
        HighCount     = _allItems.Count(i => i.Severity == "High");
        if (TotalIssues > 0)
        {
            HealthScore     = Math.Max(0, 100 - (CriticalCount * 10 + HighCount * 5));
            HealthScoreText = $"{HealthScore:F0}/100";
        }
    }
}
