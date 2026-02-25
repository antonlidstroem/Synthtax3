using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.Interfaces;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

/// <summary>
/// Definierar om Browse-knappen öppnar en fil- eller mappväljare.
/// </summary>
public enum SolutionInputMode
{
    /// <summary>Väljer en .sln-fil (används av de flesta moduler).</summary>
    SolutionFile,

    /// <summary>Väljer en mapp/katalog (används av Git-modulen).</summary>
    Folder,
}

/// <summary>
/// Bas-ViewModel för alla vyer som tar emot en solution-sökväg eller Git-repo-sökväg.
/// Hanterar:
///   - Path-egenskapen (SolutionPath / RepositoryPath mappas till samma backing field)
///   - BrowseCommand med rätt dialog beroende på InputMode
///   - IsRemoteUrl – om sökvägen är en GitHub/GitLab-URL (visas i UI)
/// </summary>
public abstract partial class AnalysisViewModelBase : ViewModelBase
{
    private string _inputPath = string.Empty;

    // ── Konfiguration (sätts av subklassen) ────────────────────────────

    /// <summary>
    /// Styr om Browse öppnar fil- eller mappväljaren.
    /// Subklassen sätter detta i sin konstruktor om den behöver Folder-läge.
    /// </summary>
    protected SolutionInputMode InputMode { get; init; } = SolutionInputMode.SolutionFile;

    // ── Publika bindningsbara egenskaper ───────────────────────────────

    /// <summary>
    /// Sökväg till .sln-fil, katalog eller GitHub/GitLab-URL.
    /// Bindas direkt av SolutionInputBar-kontrollen.
    /// </summary>
    public string InputPath
    {
        get => _inputPath;
        set
        {
            if (SetProperty(ref _inputPath, value))
            {
                OnPropertyChanged(nameof(IsRemoteUrl));
                OnPropertyChanged(nameof(PathTypeHint));
                OnInputPathChanged(value);
            }
        }
    }

    /// <summary>
    /// Alias för bakåtkompatibilitet – de flesta ViewModels exponerar SolutionPath.
    /// Binder till samma backing field som InputPath.
    /// </summary>
    public string SolutionPath
    {
        get => _inputPath;
        set => InputPath = value;
    }

    /// <summary>
    /// Alias för GitViewModel som exponerar RepositoryPath.
    /// </summary>
    public string RepositoryPath
    {
        get => _inputPath;
        set => InputPath = value;
    }

    /// <summary>True om InputPath ser ut som en http(s)://...-URL.</summary>
    public bool IsRemoteUrl => IRepositoryResolver.IsRemoteUrl(_inputPath);

    /// <summary>
    /// Kort beskrivning av sökvägens typ – visas som tooltip/hint i SolutionInputBar.
    /// </summary>
    public string PathTypeHint =>
        string.IsNullOrWhiteSpace(_inputPath) ? string.Empty
        : IsRemoteUrl ? "GitHub / GitLab URL"
        : InputMode == SolutionInputMode.Folder ? "Lokal katalog"
                                                : "Lokal .sln-fil";

    // ── Browse-kommando ────────────────────────────────────────────────

    [RelayCommand]
    private void Browse()
    {
        if (InputMode == SolutionInputMode.Folder)
        {
            var dlg = new OpenFolderDialog { Title = "Välj Git-repo" };
            if (dlg.ShowDialog() == true)
                InputPath = dlg.FolderName;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title = "Välj .sln-fil",
                Filter = "Solution files (*.sln)|*.sln",
            };
            if (dlg.ShowDialog() == true)
                InputPath = dlg.FileName;
        }
    }

    // ── Konstruktor ────────────────────────────────────────────────────

    protected AnalysisViewModelBase(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }

    // ── Utökningspunkt ─────────────────────────────────────────────────

    /// <summary>
    /// Anropas när InputPath ändras. Subklassen kan överskugga för att
    /// t.ex. rensa tidigare resultat eller trigga live-validering.
    /// </summary>
    protected virtual void OnInputPathChanged(string newPath) { }
}
