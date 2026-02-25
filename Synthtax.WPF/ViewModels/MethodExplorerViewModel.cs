using System.Collections.ObjectModel;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class MethodExplorerViewModel : AnalysisViewModelBase
{

    private string _searchText     = string.Empty;
    private string _classFilter    = string.Empty;
    private bool   _showOnlyPublic;
    private bool   _showOnlyAsync;
    private bool   _showOnlyStatic;
    private bool   _sortByComplexity;
    private bool   _hasData;
    private MethodDto? _selectedMethod;

    // ── Summary counts ─────────────────────────────────────────────────
    private int _totalMethods, _asyncCount, _staticCount, _avgComplexity;

  
    public string ClassFilter    { get => _classFilter;     set { if (SetProperty(ref _classFilter, value))     ApplyFilter(); } }
    public bool   ShowOnlyPublic { get => _showOnlyPublic;  set { if (SetProperty(ref _showOnlyPublic, value))  ApplyFilter(); } }
    public bool   ShowOnlyAsync  { get => _showOnlyAsync;   set { if (SetProperty(ref _showOnlyAsync, value))   ApplyFilter(); } }
    public bool   ShowOnlyStatic { get => _showOnlyStatic;  set { if (SetProperty(ref _showOnlyStatic, value))  ApplyFilter(); } }
    public bool   SortByComplexity { get => _sortByComplexity; set { if (SetProperty(ref _sortByComplexity, value)) ApplyFilter(); } }
    public bool   HasData        { get => _hasData;         private set => SetProperty(ref _hasData, value); }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public MethodDto? SelectedMethod
    {
        get => _selectedMethod;
        set => SetProperty(ref _selectedMethod, value);
    }

    public int TotalMethods   { get => _totalMethods;   private set => SetProperty(ref _totalMethods, value); }
    public int AsyncCount     { get => _asyncCount;     private set => SetProperty(ref _asyncCount, value); }
    public int StaticCount    { get => _staticCount;    private set => SetProperty(ref _staticCount, value); }
    public int AvgComplexity  { get => _avgComplexity;  private set => SetProperty(ref _avgComplexity, value); }

    public ObservableCollection<MethodDto> FilteredMethods { get; } = new();

    // Distinct class names for the class filter dropdown
    public ObservableCollection<string> ClassNames { get; } = new();

    private List<MethodDto> _allMethods = new();

    public MethodExplorerViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }


    [RelayCommand]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        if (!ValidateInputPath(out var validationError))
        {
            SetError(validationError!);
            return;
        }

        await RunSafeAsync(async () =>
        {
            _allMethods.Clear();
            FilteredMethods.Clear();
            ClassNames.Clear();
            HasData = false;

            var result = await Api.GetAsync<MethodExplorerResultDto>(
                $"api/methodexplorer/methods?solutionPath={Uri.EscapeDataString(SolutionPath)}");

            if (result is null)
            {
                SetError("Kunde inte ladda metoder. Kontrollera solution-sökvägen.");
                return;
            }

            // ... process result
        }, "Status_Analyzing");
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _searchText     = string.Empty; OnPropertyChanged(nameof(SearchText));
        _classFilter    = "Alla klasser"; OnPropertyChanged(nameof(ClassFilter));
        _showOnlyPublic  = false; OnPropertyChanged(nameof(ShowOnlyPublic));
        _showOnlyAsync   = false; OnPropertyChanged(nameof(ShowOnlyAsync));
        _showOnlyStatic  = false; OnPropertyChanged(nameof(ShowOnlyStatic));
        _sortByComplexity = false; OnPropertyChanged(nameof(SortByComplexity));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredMethods.Clear();

        var query = _allMethods.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
            query = query.Where(m =>
                m.MethodName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || m.ClassName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || m.FullSignature.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(_classFilter) && _classFilter != "Alla klasser")
            query = query.Where(m =>
                m.ClassName.Equals(_classFilter, StringComparison.OrdinalIgnoreCase));

        if (_showOnlyPublic)  query = query.Where(m => m.IsPublic);
        if (_showOnlyAsync)   query = query.Where(m => m.IsAsync);
        if (_showOnlyStatic)  query = query.Where(m => m.IsStatic);

        query = _sortByComplexity
            ? query.OrderByDescending(m => m.CyclomaticComplexity)
            : query.OrderBy(m => m.ClassName).ThenBy(m => m.MethodName);

        foreach (var m in query)
            FilteredMethods.Add(m);
    }
}
