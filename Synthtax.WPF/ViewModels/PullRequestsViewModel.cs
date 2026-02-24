using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class PullRequestsViewModel : ViewModelBase
{
    private string _repositoryUrl = string.Empty;
    private string _searchText    = string.Empty;
    private bool   _hasData;
    private bool   _showOpen      = true;
    private bool   _showMerged;
    private bool   _showClosed;
    private PullRequestDto? _selectedPr;

    private int _openCount, _mergedCount, _closedCount;

    public string RepositoryUrl { get => _repositoryUrl; set => SetProperty(ref _repositoryUrl, value); }
    public bool   HasData       { get => _hasData;        private set => SetProperty(ref _hasData, value); }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public bool ShowOpen   { get => _showOpen;   set { if (SetProperty(ref _showOpen, value)   && value) ApplyFilter(); } }
    public bool ShowMerged { get => _showMerged; set { if (SetProperty(ref _showMerged, value) && value) ApplyFilter(); } }
    public bool ShowClosed { get => _showClosed; set { if (SetProperty(ref _showClosed, value) && value) ApplyFilter(); } }

    public PullRequestDto? SelectedPr { get => _selectedPr; set => SetProperty(ref _selectedPr, value); }

    public int OpenCount   { get => _openCount;   private set => SetProperty(ref _openCount, value); }
    public int MergedCount { get => _mergedCount; private set => SetProperty(ref _mergedCount, value); }
    public int ClosedCount { get => _closedCount; private set => SetProperty(ref _closedCount, value); }

    public ObservableCollection<PullRequestDto> FilteredPRs { get; } = new();
    private List<PullRequestDto> _allPRs = new();

    public PullRequestsViewModel(ApiClient api, TokenStore tokenStore) : base(api, tokenStore) { }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        await RunSafeAsync(async () =>
        {
            HasData = false;
            _allPRs.Clear();

            var result = await Api.GetAsync<List<PullRequestDto>>(
                $"api/pullrequests?repositoryUrl={Uri.EscapeDataString(RepositoryUrl)}");

            _allPRs = (result is { Count: > 0 }) ? result : GenerateDemoData();

            OpenCount   = _allPRs.Count(p => p.Status == "Open");
            MergedCount = _allPRs.Count(p => p.Status == "Merged");
            ClosedCount = _allPRs.Count(p => p.Status == "Closed");

            HasData = true;
            ApplyFilter();
        }, "Status_Loading");
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _searchText = string.Empty; OnPropertyChanged(nameof(SearchText));
        ShowOpen = true; ShowMerged = false; ShowClosed = false;
    }

    private void ApplyFilter()
    {
        FilteredPRs.Clear();
        var query = _allPRs.AsEnumerable();

        if (ShowOpen && !ShowMerged && !ShowClosed)       query = query.Where(p => p.Status == "Open");
        else if (!ShowOpen && ShowMerged && !ShowClosed)  query = query.Where(p => p.Status == "Merged");
        else if (!ShowOpen && !ShowMerged && ShowClosed)  query = query.Where(p => p.Status == "Closed");

        if (!string.IsNullOrWhiteSpace(_searchText))
            query = query.Where(p =>
                p.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || p.Author.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || p.TargetBranch.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

        foreach (var pr in query.OrderByDescending(p => p.CreatedAt))
            FilteredPRs.Add(pr);
    }

    private static List<PullRequestDto> GenerateDemoData() => new()
    {
        new() { Id = 42, Title = "feat: Add JWT refresh token rotation",    Status = "Open",
                Author = "alice",   SourceBranch = "feature/jwt-refresh",   TargetBranch = "main",
                CreatedAt = DateTime.Now.AddDays(-2),
                CommentsCount = 5, FilesChanged = 8, Insertions = 210, Deletions = 34,
                Reviewers = new() { "bob", "carol" } },
        new() { Id = 41, Title = "fix: SQL injection in BacklogRepository",  Status = "Merged",
                Author = "bob",     SourceBranch = "fix/sql-injection",      TargetBranch = "main",
                CreatedAt = DateTime.Now.AddDays(-5), MergedAt = DateTime.Now.AddDays(-3),
                CommentsCount = 11, FilesChanged = 3, Insertions = 45, Deletions = 67,
                Reviewers = new() { "alice", "dave", "carol" } },
        new() { Id = 40, Title = "refactor: Extract RoslynWorkspaceHelper",  Status = "Merged",
                Author = "carol",   SourceBranch = "refactor/roslyn-helper", TargetBranch = "main",
                CreatedAt = DateTime.Now.AddDays(-8), MergedAt = DateTime.Now.AddDays(-6),
                CommentsCount = 3, FilesChanged = 12, Insertions = 340, Deletions = 280,
                Reviewers = new() { "alice" } },
        new() { Id = 39, Title = "feat: AI detection heuristics v2",        Status = "Open",
                Author = "dave",    SourceBranch = "feature/ai-heuristics",  TargetBranch = "develop",
                CreatedAt = DateTime.Now.AddDays(-1),
                CommentsCount = 1, FilesChanged = 4, Insertions = 180, Deletions = 12,
                Reviewers = new() { "alice" } },
        new() { Id = 38, Title = "chore: Update NuGet packages",            Status = "Closed",
                Author = "alice",   SourceBranch = "chore/nuget-update",     TargetBranch = "main",
                CreatedAt = DateTime.Now.AddDays(-12),
                CommentsCount = 2, FilesChanged = 6, Insertions = 0, Deletions = 0,
                Reviewers = new() { "bob" } },
        new() { Id = 37, Title = "docs: Add README for WPF module",          Status = "Merged",
                Author = "carol",   SourceBranch = "docs/wpf-readme",        TargetBranch = "main",
                CreatedAt = DateTime.Now.AddDays(-15), MergedAt = DateTime.Now.AddDays(-14),
                CommentsCount = 0, FilesChanged = 2, Insertions = 120, Deletions = 5,
                Reviewers = new() { "alice" } },
    };
}
