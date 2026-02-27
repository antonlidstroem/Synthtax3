using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public class GitRowItem
{
    public string ShortSha { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime AuthoredAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
}

public partial class GitViewModel : AnalysisViewModelBase
{
    // ── RepositoryPath, Browse() → ärvs. InputMode = Folder. ──────────

    private int _maxCommits = 100;
    private string _currentBranch = string.Empty;
    private int _totalCommits, _totalBranches, _totalContributors;
    private bool _showCommits = true, _showChurn, _showBusFactor, _showBranches;

    public int MaxCommits { get => _maxCommits; set => SetProperty(ref _maxCommits, value); }
    public string CurrentBranch { get => _currentBranch; private set => SetProperty(ref _currentBranch, value); }
    public int TotalCommits { get => _totalCommits; private set => SetProperty(ref _totalCommits, value); }
    public int TotalBranches { get => _totalBranches; private set => SetProperty(ref _totalBranches, value); }
    public int TotalContributors { get => _totalContributors; private set => SetProperty(ref _totalContributors, value); }

    public List<int> CommitLimits { get; } = new() { 50, 100, 250, 500 };

    public bool ShowCommits { get => _showCommits; set { if (SetProperty(ref _showCommits, value) && value) RefreshRows(); } }
    public bool ShowChurn { get => _showChurn; set { if (SetProperty(ref _showChurn, value) && value) RefreshRows(); } }
    public bool ShowBusFactor { get => _showBusFactor; set { if (SetProperty(ref _showBusFactor, value) && value) RefreshRows(); } }
    public bool ShowBranches { get => _showBranches; set { if (SetProperty(ref _showBranches, value) && value) RefreshRows(); } }

    public ObservableCollection<GitRowItem> CurrentRows { get; } = new();

    private List<GitCommitDto> _commits = new();
    private List<GitChurnDto> _churn = new();
    private List<BusFactorDto> _busFactor = new();
    private List<GitBranchDto> _branches = new();

    public GitViewModel(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore)
    {
        InputMode = SolutionInputMode.Folder;   // ← öppnar mappväljare
    }

    [RelayCommand]
    private async Task AnalyzeAsync(CancellationToken ct)
    {
        // IMPROVED: Validera input först
        if (!ValidateInputPath(out var validationError))
        {
            SetError(validationError!);
            return;
        }

        await RunSafeAsync(async () =>
        {
            var result = await Api.GetAsync<GitAnalysisResultDto>(
                $"api/git/analyze?repositoryPath={Uri.EscapeDataString(RepositoryPath)}&maxCommits={MaxCommits}");

            // IMPROVED: Visa specifik felmeddelande om API returnerar null
            if (result is null)
            {
                SetError(
                    IsRemoteUrl
                    ? "Kunde inte klona repository från GitHub. Kontrollera:\n• URL är korrekt\n• Nätverket fungerar\n• Git är installerat på servern"
                    : "Kunde inte analysera Git-repository. Kontrollera:\n• Sökvägen är korrekt\n• Det är ett Git-repository (.git-mapp finns)\n• Du har läsbehörighet");
                return;
            }

            CurrentBranch = result.CurrentBranch;
            TotalCommits = result.TotalCommits;
            TotalBranches = result.TotalBranches;
            TotalContributors = result.TotalContributors;
            _commits = result.RecentCommits;
            _churn = result.FileChurn;
            _busFactor = result.BusFactor;
            _branches = result.Branches;
            RefreshRows();
        }, "Status_Analyzing");
    }

    private void RefreshRows()
    {
        CurrentRows.Clear();
        if (ShowCommits)
            foreach (var c in _commits)
                CurrentRows.Add(new GitRowItem
                {
                    ShortSha = c.ShortSha,
                    AuthorName = c.AuthorName,
                    AuthoredAt = c.AuthoredAt,
                    Message = c.Message,
                    ChangeSummary = $"+{c.Insertions}/-{c.Deletions}",
                });
        else if (ShowChurn)
            foreach (var c in _churn.Take(200))
                CurrentRows.Add(new GitRowItem
                {
                    ShortSha = c.CommitCount.ToString(),
                    AuthorName = string.Join(", ", c.Authors.Take(3)),
                    AuthoredAt = c.LastChanged,
                    Message = c.FileName,
                    ChangeSummary = $"+{c.TotalInsertions}/-{c.TotalDeletions}",
                });
        else if (ShowBusFactor)
            foreach (var b in _busFactor)
                CurrentRows.Add(new GitRowItem
                {
                    ShortSha = $"{b.Percentage}%",
                    AuthorName = b.AuthorName,
                    Message = string.Join(", ", b.PrimaryFiles.Take(3)),
                    ChangeSummary = b.CommitCount.ToString(),
                });
        else if (ShowBranches)
            foreach (var br in _branches)
                CurrentRows.Add(new GitRowItem
                {
                    ShortSha = br.TipSha,
                    AuthorName = br.LastCommitAuthor,
                    AuthoredAt = br.LastCommitDate,
                    Message = br.Name,
                    ChangeSummary = br.IsRemote ? "remote" : "local",
                });
    }
}
