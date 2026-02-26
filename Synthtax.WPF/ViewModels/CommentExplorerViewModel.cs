using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class CommentExplorerViewModel : AnalysisViewModelBase
{
    private string _searchText = string.Empty;
    private bool _hasData;
    private bool _showAll = true;
    private bool _showTodos;
    private bool _showXmlDoc;
    private bool _showRegions;
    private CommentDto? _selectedComment;

    // ── Summary counts ────────────────────────────────────────────────────
    private int _totalComments, _xmlDocCount, _todoCount, _regionCount;

    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }

    public bool ShowAll { get => _showAll; set { if (SetProperty(ref _showAll, value) && value) ApplyFilter(); } }
    public bool ShowTodos { get => _showTodos; set { if (SetProperty(ref _showTodos, value) && value) ApplyFilter(); } }
    public bool ShowXmlDoc { get => _showXmlDoc; set { if (SetProperty(ref _showXmlDoc, value) && value) ApplyFilter(); } }
    public bool ShowRegions { get => _showRegions; set { if (SetProperty(ref _showRegions, value) && value) ApplyFilter(); } }

    public CommentDto? SelectedComment { get => _selectedComment; set => SetProperty(ref _selectedComment, value); }

    public int TotalComments { get => _totalComments; private set => SetProperty(ref _totalComments, value); }
    public int XmlDocCount { get => _xmlDocCount; private set => SetProperty(ref _xmlDocCount, value); }
    public int TodoCount { get => _todoCount; private set => SetProperty(ref _todoCount, value); }
    public int RegionCount { get => _regionCount; private set => SetProperty(ref _regionCount, value); }

    public ObservableCollection<CommentDto> DisplayComments { get; } = new();
    public ObservableCollection<RegionDto> DisplayRegions { get; } = new();

    private List<CommentDto> _allComments = new();
    private List<RegionDto> _allRegions = new();

    public CommentExplorerViewModel(ApiClient api, TokenStore tokenStore)
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
            _allComments.Clear();
            _allRegions.Clear();
            DisplayComments.Clear();
            DisplayRegions.Clear();
            HasData = false;

            // BUG FIX: The route was "api/commentexplorer/all" which does not
            // exist. The controller exposes:
            //   GET api/commentexplorer/comments   ← correct endpoint
            //   GET api/commentexplorer/todos
            //   GET api/commentexplorer/regions
            // Using /all returned 404, silently giving a null result.
            var result = await Api.GetAsync<CommentExplorerResultDto>(
                $"api/commentexplorer/comments?solutionPath={Uri.EscapeDataString(SolutionPath)}");

            if (result is null)
            {
                SetError("Kunde inte analysera kommentarer. Kontrollera solution-sökvägen.");
                return;
            }

            _allComments = result.Comments;
            _allRegions = result.Regions;

            TotalComments = result.TotalComments;
            XmlDocCount = result.XmlDocComments;
            TodoCount = result.TodoComments;
            RegionCount = result.TotalRegions;

            HasData = true;
            ApplyFilter();
        }, "Status_Analyzing");
    }

    private void ApplyFilter()
    {
        DisplayComments.Clear();
        DisplayRegions.Clear();

        if (ShowRegions)
        {
            foreach (var r in _allRegions)
                DisplayRegions.Add(r);
            return;
        }

        var query = _allComments.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_searchText))
            query = query.Where(c =>
                c.Content.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || c.FileName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || (c.AssociatedMember?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ?? false));

        if (ShowTodos)
            query = query.Where(c =>
                c.Content.Contains("TODO", StringComparison.OrdinalIgnoreCase)
                || c.Content.Contains("FIXME", StringComparison.OrdinalIgnoreCase)
                || c.Content.Contains("HACK", StringComparison.OrdinalIgnoreCase));
        else if (ShowXmlDoc)
            query = query.Where(c => c.CommentType == "XmlDoc");

        foreach (var c in query.OrderBy(c => c.FileName).ThenBy(c => c.LineNumber))
            DisplayComments.Add(c);
    }
}
