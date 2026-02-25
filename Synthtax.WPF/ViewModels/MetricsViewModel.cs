using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class MetricsViewModel : AnalysisViewModelBase
{
    // ── BORTTAGET: private string _solutionPath och public string SolutionPath ──
    // Skuggade basklassens property precis som i CodeAnalysisViewModel.

    private int _totalLoc;
    private int _totalFiles;
    private double _avgComplexity;
    private double _avgMaintainability;
    private FileMetricsDto? _selectedFile;

    public int TotalLoc { get => _totalLoc; private set => SetProperty(ref _totalLoc, value); }
    public int TotalFiles { get => _totalFiles; private set => SetProperty(ref _totalFiles, value); }
    public double AvgComplexity { get => _avgComplexity; private set => SetProperty(ref _avgComplexity, value); }
    public double AvgMaintainability { get => _avgMaintainability; private set => SetProperty(ref _avgMaintainability, value); }
    public FileMetricsDto? SelectedFile { get => _selectedFile; set => SetProperty(ref _selectedFile, value); }

    public ObservableCollection<FileMetricsDto> FileMetrics { get; } = new();

    public MetricsViewModel(ApiClient api, TokenStore tokenStore) : base(api, tokenStore) { }

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
            FileMetrics.Clear();

            var result = await Api.PostAsync<MetricsResultDto>(
                "api/metrics/solution",
                new AnalysisRequestDto { SolutionPath = SolutionPath }, ct: ct);

            if (result is null)
            {
                SetError("Kunde inte beräkna metrics. Kontrollera solution-sökvägen.");
                return;
            }

            // Fyll UI med resultatet
            TotalLoc = result.TotalLinesOfCode;
            TotalFiles = result.TotalFiles;

            AvgComplexity = result.OverallCyclomaticComplexity;
            AvgMaintainability = result.OverallMaintainabilityIndex;

            foreach (var f in result.Files ?? new())
                FileMetrics.Add(f);

        }, "Status_Analyzing");
    }
}
