using System.Collections.ObjectModel;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class AIDetectionViewModel : ViewModelBase
{
    private string _solutionPath    = string.Empty;
    private string _searchText      = string.Empty;
    private bool   _hasData;
    private bool   _showAll         = true;
    private bool   _showHighOnly;
    private AIDetectionFileResultDto? _selectedFile;

    // ── Summary ─────────────────────────────────────────────────────────
    private double _overallScore;
    private string _overallVerdict = string.Empty;
    private int    _filesAnalyzed, _filesHighScore;

    public string SolutionPath { get => _solutionPath; set => SetProperty(ref _solutionPath, value); }
    public bool   HasData      { get => _hasData;       private set => SetProperty(ref _hasData, value); }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public bool ShowAll      { get => _showAll;      set { if (SetProperty(ref _showAll, value)      && value) ApplyFilter(); } }
    public bool ShowHighOnly { get => _showHighOnly;  set { if (SetProperty(ref _showHighOnly, value) && value) ApplyFilter(); } }

    public double OverallScore   { get => _overallScore;   private set => SetProperty(ref _overallScore, value); }
    public string OverallVerdict { get => _overallVerdict; private set => SetProperty(ref _overallVerdict, value); }
    public int    FilesAnalyzed  { get => _filesAnalyzed;  private set => SetProperty(ref _filesAnalyzed, value); }
    public int    FilesHighScore { get => _filesHighScore; private set => SetProperty(ref _filesHighScore, value); }

    public AIDetectionFileResultDto? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
                RefreshSignals();
        }
    }

    public ObservableCollection<AIDetectionFileResultDto> FilteredFiles { get; } = new();
    public ObservableCollection<AIDetectionSignalDto>     Signals       { get; } = new();

    private List<AIDetectionFileResultDto> _allFiles = new();

    public AIDetectionViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Välj .sln-fil",
            Filter = "Solution files (*.sln)|*.sln"
        };
        if (dlg.ShowDialog() == true)
            SolutionPath = dlg.FileName;
    }

    [RelayCommand]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SolutionPath)) return;

        await RunSafeAsync(async () =>
        {
            _allFiles.Clear();
            FilteredFiles.Clear();
            Signals.Clear();
            HasData = false;

            var result = await Api.PostAsync<AIDetectionResultDto>(
                "api/aidetection/analyze",
                new AnalysisRequestDto { SolutionPath = SolutionPath },
                ct: ct);

            if (result is null) return;

            _allFiles        = result.FileResults;
            OverallScore     = result.OverallScore;
            OverallVerdict   = result.OverallVerdict;
            FilesAnalyzed    = result.FilesAnalyzed;
            FilesHighScore   = result.FilesWithHighScore;

            HasData = true;
            ApplyFilter();
        }, "Status_Analyzing");
    }

    private void ApplyFilter()
    {
        FilteredFiles.Clear();

        var query = _allFiles.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
            query = query.Where(f =>
                f.FileName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || f.Verdict.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        if (_showHighOnly)
            query = query.Where(f => f.AILikelihoodScore >= 0.65);

        // Always sort highest score first
        query = query.OrderByDescending(f => f.AILikelihoodScore);

        foreach (var f in query)
            FilteredFiles.Add(f);
    }

    private void RefreshSignals()
    {
        Signals.Clear();
        if (_selectedFile is null) return;
        foreach (var s in _selectedFile.Signals.OrderByDescending(s => s.Weight))
            Signals.Add(s);
    }
}
