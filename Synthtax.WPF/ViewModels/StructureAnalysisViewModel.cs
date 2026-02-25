using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.DTOs;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public partial class StructureTreeNode : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;

    public StructureNodeDto Dto { get; }
    public ObservableCollection<StructureTreeNode> Children { get; } = new();

    public string Name => Dto.Name;
    public string NodeType => Dto.NodeType;
    public string Modifier => Dto.Modifier ?? string.Empty;
    public string ReturnType => Dto.ReturnType ?? string.Empty;
    public string FilePath => Dto.FilePath ?? string.Empty;
    public int LineNumber => Dto.LineNumber ?? 0;
    public bool IsAbstract => Dto.IsAbstract;
    public bool IsStatic => Dto.IsStatic;

    public string Icon => NodeType switch
    {
        "Solution" => "📦",
        "Project" => "📁",
        "Namespace" => "🗂",
        "Class" => "🟦",
        "Interface" => "🟩",
        "Struct" => "🟧",
        "Record" => "🟪",
        "Enum" => "🔢",
        "Method" => "⚙",
        "Property" => "◈",
        "Field" => "▪",
        _ => "•"
    };

    public string TypeColour => NodeType switch
    {
        "Solution" or "Project" => "#1A2744",
        "Namespace" => "#5A6480",
        "Class" => "#3B7DD8",
        "Interface" => "#1DB36B",
        "Struct" or "Record" => "#8B5CF6",
        "Enum" => "#F59E0B",
        "Method" => "#374151",
        "Property" => "#6B7280",
        "Field" => "#9AA3B8",
        _ => "#374151"
    };

    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public StructureTreeNode(StructureNodeDto dto)
    {
        Dto = dto;
        foreach (var child in dto.Children)
            Children.Add(new StructureTreeNode(child));
        if (NodeType is "Solution" or "Project" or "Namespace")
            IsExpanded = true;
    }
}

public partial class StructureAnalysisViewModel : AnalysisViewModelBase
{
    private string _searchText = string.Empty;
    private bool _hasData;
    private bool _showMethods = true;
    private bool _showProperties = true;
    private bool _showFields = false;
    private StructureTreeNode? _selectedNode;
    private int _totalProjects, _totalClasses, _totalMethods, _totalInterfaces;

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) ApplyFilter(); }
    }
    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }
    public bool ShowMethods
    {
        get => _showMethods;
        set { if (SetProperty(ref _showMethods, value)) ApplyFilter(); }
    }
    public bool ShowProperties
    {
        get => _showProperties;
        set { if (SetProperty(ref _showProperties, value)) ApplyFilter(); }
    }
    public bool ShowFields
    {
        get => _showFields;
        set { if (SetProperty(ref _showFields, value)) ApplyFilter(); }
    }
    public StructureTreeNode? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public int TotalProjects { get => _totalProjects; private set => SetProperty(ref _totalProjects, value); }
    public int TotalClasses { get => _totalClasses; private set => SetProperty(ref _totalClasses, value); }
    public int TotalMethods { get => _totalMethods; private set => SetProperty(ref _totalMethods, value); }
    public int TotalInterfaces { get => _totalInterfaces; private set => SetProperty(ref _totalInterfaces, value); }

    public ObservableCollection<StructureTreeNode> TreeNodes { get; } = new();

    private StructureAnalysisResultDto? _lastResult;

    public StructureAnalysisViewModel(ApiClient api, TokenStore tokenStore)
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
            TreeNodes.Clear();
            HasData = false;
            _lastResult = null;

            var result = await Api.GetAsync<StructureAnalysisResultDto>(
                $"api/structure?solutionPath={Uri.EscapeDataString(SolutionPath)}");

            if (result?.RootNode is null)
            {
                SetError("Kunde inte analysera solution-struktur. Kontrollera sökvägen.");
                return;
            }

            _lastResult = result;

            // Räkna noder för stat-korten
            BuildCounts(result.RootNode);

            // Bygg trädet (med eventuella filter applicerade)
            ApplyFilter();

            HasData = true;

        }, "Status_Analyzing");
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var n in TreeNodes)
            ExpandRecursive(n, true);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var n in TreeNodes)
            ExpandRecursive(n, false);
    }

    private void BuildCounts(StructureNodeDto root)
    {
        var all = Flatten(root).ToList();
        TotalProjects = all.Count(n => n.NodeType == "Project");
        TotalClasses = all.Count(n => n.NodeType is "Class" or "Record" or "Struct");
        TotalMethods = all.Count(n => n.NodeType == "Method");
        TotalInterfaces = all.Count(n => n.NodeType == "Interface");
    }

    private void ApplyFilter()
    {
        TreeNodes.Clear();
        if (_lastResult?.RootNode is null) return;

        var root = BuildFilteredNode(_lastResult.RootNode);
        if (root is not null)
            TreeNodes.Add(root);
    }

    private StructureTreeNode? BuildFilteredNode(StructureNodeDto dto)
    {
        if (dto.NodeType == "Method" && !_showMethods) return null;
        if (dto.NodeType == "Property" && !_showProperties) return null;
        if (dto.NodeType == "Field" && !_showFields) return null;

        var isLeaf = dto.Children.Count == 0
            || dto.NodeType is "Method" or "Property" or "Field";

        if (!string.IsNullOrWhiteSpace(_searchText) && isLeaf)
        {
            if (!dto.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        var node = new StructureTreeNode(dto);
        node.Children.Clear();
        foreach (var child in dto.Children)
        {
            var filtered = BuildFilteredNode(child);
            if (filtered is not null)
                node.Children.Add(filtered);
        }

        if (!string.IsNullOrWhiteSpace(_searchText) && !isLeaf && node.Children.Count == 0)
            return null;

        return node;
    }

    private static IEnumerable<StructureNodeDto> Flatten(StructureNodeDto node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var desc in Flatten(child))
                yield return desc;
    }

    private static void ExpandRecursive(StructureTreeNode node, bool expand)
    {
        node.IsExpanded = expand;
        foreach (var child in node.Children)
            ExpandRecursive(child, expand);
    }
}
