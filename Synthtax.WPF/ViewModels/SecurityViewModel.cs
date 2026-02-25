using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Synthtax.Core.DTOs;
using Synthtax.Core.Enums;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class SecurityViewModel : AnalysisViewModelBase
{
    private int _criticalCount, _highCount, _mediumCount, _lowCount;
    private bool _showAll = true, _showCredentials, _showSql, _showRandom, _showCt;
    private SecurityIssueDto? _selectedIssue;

    public int CriticalCount { get => _criticalCount; private set => SetProperty(ref _criticalCount, value); }
    public int HighCount { get => _highCount; private set => SetProperty(ref _highCount, value); }
    public int MediumCount { get => _mediumCount; private set => SetProperty(ref _mediumCount, value); }
    public int LowCount { get => _lowCount; private set => SetProperty(ref _lowCount, value); }

    public bool ShowAll { get => _showAll; set { if (SetProperty(ref _showAll, value) && value) RefreshIssues(); } }
    public bool ShowCredentials { get => _showCredentials; set { if (SetProperty(ref _showCredentials, value) && value) RefreshIssues(); } }
    public bool ShowSql { get => _showSql; set { if (SetProperty(ref _showSql, value) && value) RefreshIssues(); } }
    public bool ShowRandom { get => _showRandom; set { if (SetProperty(ref _showRandom, value) && value) RefreshIssues(); } }
    public bool ShowCt { get => _showCt; set { if (SetProperty(ref _showCt, value) && value) RefreshIssues(); } }

    public SecurityIssueDto? SelectedIssue { get => _selectedIssue; set => SetProperty(ref _selectedIssue, value); }

    public ObservableCollection<SecurityIssueDto> CurrentIssues { get; } = new();

    private SecurityAnalysisResultDto? _lastResult;

    public SecurityViewModel(ApiClient api, TokenStore tokenStore) : base(api, tokenStore) { }

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
            CurrentIssues.Clear();
            _lastResult = null;

            _lastResult = await Api.PostAsync<SecurityAnalysisResultDto>(
                "api/security/analyze",
                new AnalysisRequestDto { SolutionPath = SolutionPath }, ct: ct);

            if (_lastResult is null)
            {
                SetError("Kunde inte analysera säkerhet. Kontrollera solution-sökvägen.");
                return;
            }

            // Räkna per allvarlighetsgrad
            var all = _lastResult.AllIssues ?? new();
            CriticalCount = all.Count(i => i.Severity == Severity.Critical);
            HighCount = all.Count(i => i.Severity == Severity.High);
            MediumCount = all.Count(i => i.Severity == Severity.Medium);
            LowCount = all.Count(i => i.Severity == Severity.Low);

            // Visa aktiv flik
            RefreshIssues();

        }, "Status_Analyzing");
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (_lastResult is null) return;
        var (bytes, _, fileName) = await Api.DownloadAsync("api/export/pdf/security", _lastResult);
        if (bytes is not null)
        {
            var dlg = new SaveFileDialog { FileName = fileName ?? "Security.pdf" };
            if (dlg.ShowDialog() == true)
                File.WriteAllBytes(dlg.FileName, bytes);
        }
    }

    private void RefreshIssues()
    {
        CurrentIssues.Clear();
        if (_lastResult is null) return;

        var source = ShowAll ? (_lastResult.AllIssues ?? new())
                   : ShowCredentials ? (_lastResult.HardcodedCredentials ?? new())
                   : ShowSql ? (_lastResult.SqlInjectionRisks ?? new())
                   : ShowRandom ? (_lastResult.InsecureRandomUsage ?? new())
                                     : (_lastResult.MissingCancellationTokens ?? new());

        foreach (var i in source) CurrentIssues.Add(i);
    }
}
