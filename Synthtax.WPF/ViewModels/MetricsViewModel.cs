using System.Collections.ObjectModel;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class MetricsViewModel : ViewModelBase
{
    private string _solutionPath = string.Empty;
    private int _totalLoc;
    private int _totalFiles;
    private double _avgComplexity;
    private double _avgMaintainability;
    private FileMetricsDto? _selectedFile;

    public string SolutionPath    { get => _solutionPath;         set => SetProperty(ref _solutionPath, value); }
    public int    TotalLoc        { get => _totalLoc;             private set => SetProperty(ref _totalLoc, value); }
    public int    TotalFiles      { get => _totalFiles;           private set => SetProperty(ref _totalFiles, value); }
    public double AvgComplexity   { get => _avgComplexity;        private set => SetProperty(ref _avgComplexity, value); }
    public double AvgMaintainability { get => _avgMaintainability;private set => SetProperty(ref _avgMaintainability, value); }
    public FileMetricsDto? SelectedFile { get => _selectedFile;   set => SetProperty(ref _selectedFile, value); }

    public ObservableCollection<FileMetricsDto> FileMetrics { get; } = new();

    public MetricsViewModel(ApiClient api, TokenStore tokenStore) : base(api, tokenStore) { }

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog { Title = "Välj .sln-fil", Filter = "Solution files (*.sln)|*.sln" };
        if (dlg.ShowDialog() == true) SolutionPath = dlg.FileName;
    }

    [RelayCommand]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SolutionPath)) return;
        await RunSafeAsync(async () =>
        {
            FileMetrics.Clear();
            var result = await Api.PostAsync<MetricsResultDto>("api/metrics/solution",
                new AnalysisRequestDto { SolutionPath = SolutionPath }, ct: ct);

            if (result is not null)
            {
                TotalLoc = result.TotalLinesOfCode;
                TotalFiles = result.TotalFiles;
                AvgComplexity = result.OverallCyclomaticComplexity;
                AvgMaintainability = result.OverallMaintainabilityIndex;
                foreach (var f in result.Files.OrderByDescending(f => f.LinesOfCode))
                    FileMetrics.Add(f);
            }
        }, "Status_Analyzing");
    }
}
