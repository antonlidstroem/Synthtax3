using System.Collections.ObjectModel;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;
using System.IO;

namespace Synthtax.WPF.ViewModels;

public partial class CodeAnalysisViewModel : ViewModelBase
{
    private string _solutionPath = string.Empty;
    private bool _showLongMethods = true;
    private bool _showDeadVariables;
    private bool _showUnnecessaryUsings;
    private CodeIssueDto? _selectedIssue;

    public string SolutionPath
    {
        get => _solutionPath;
        set => SetProperty(ref _solutionPath, value);
    }

    public bool ShowLongMethods
    {
        get => _showLongMethods;
        set { if (SetProperty(ref _showLongMethods, value)) RefreshCurrentIssues(); }
    }

    public bool ShowDeadVariables
    {
        get => _showDeadVariables;
        set { if (SetProperty(ref _showDeadVariables, value)) RefreshCurrentIssues(); }
    }

    public bool ShowUnnecessaryUsings
    {
        get => _showUnnecessaryUsings;
        set { if (SetProperty(ref _showUnnecessaryUsings, value)) RefreshCurrentIssues(); }
    }

    public CodeIssueDto? SelectedIssue
    {
        get => _selectedIssue;
        set => SetProperty(ref _selectedIssue, value);
    }

    public int TotalIssues => LongMethods.Count + DeadVariables.Count + UnnecessaryUsings.Count;
    public int LongMethodCount => LongMethods.Count;
    public int DeadVariableCount => DeadVariables.Count;
    public int UnnecessaryUsingCount => UnnecessaryUsings.Count;

    private List<CodeIssueDto> LongMethods { get; } = new();
    private List<CodeIssueDto> DeadVariables { get; } = new();
    private List<CodeIssueDto> UnnecessaryUsings { get; } = new();

    public ObservableCollection<CodeIssueDto> CurrentIssues { get; } = new();

    public CodeAnalysisViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Välj .sln-fil",
            Filter = "Solution files (*.sln)|*.sln"
        };
        if (dlg.ShowDialog() == true)
            SolutionPath = dlg.FileName;
    }

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
            // ...clear collections...
            var result = await Api.PostAsync<CodeAnalysisResultDto>(
                "api/codeanalysis/solution",
                new AnalysisRequestDto { SolutionPath = SolutionPath }, ct: ct);

            if (result is null)
            {
                SetError("Kunde inte analysera solution. Kontrollera att sökvägen är korrekt och är en giltig Visual Studio-solution.");
                return;
            }

            // ... process result
        }, "Status_Analyzing");
    }

    [RelayCommand]
    private async Task ExportCsvAsync() => await ExportAsync("csv");
    [RelayCommand]
    private async Task ExportJsonAsync() => await ExportAsync("json");
    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync("pdf");

    private async Task ExportAsync(string format)
    {
        var allIssues = LongMethods.Concat(DeadVariables).Concat(UnnecessaryUsings).ToList();
        if (!allIssues.Any()) return;

        if (format == "pdf")
        {
            var (bytes, _, fileName) = await Api.DownloadAsync("api/export/pdf/code-analysis", new CodeAnalysisResultDto
            {
                LongMethods = LongMethods,
                DeadVariables = DeadVariables,
                UnnecessaryUsings = UnnecessaryUsings
            });
            if (bytes is not null) SaveFile(bytes, fileName ?? "CodeAnalysis.pdf");
        }
        else
        {
            var (bytes, _, fileName) = await Api.DownloadAsync("api/export",
                new { ModuleName = "CodeAnalysis", Format = format == "csv" ? 0 : 1, Data = allIssues });
            if (bytes is not null) SaveFile(bytes, fileName ?? $"CodeAnalysis.{format}");
        }
    }

    private void RefreshCurrentIssues()
    {
        CurrentIssues.Clear();
        var source = ShowLongMethods ? LongMethods
                   : ShowDeadVariables ? DeadVariables
                   : UnnecessaryUsings;
        foreach (var item in source) CurrentIssues.Add(item);
    }

    private static void SaveFile(byte[] bytes, string fileName)
    {
        var dlg = new SaveFileDialog { FileName = fileName };
        if (dlg.ShowDialog() == true)
            File.WriteAllBytes(dlg.FileName, bytes);
    }


}
