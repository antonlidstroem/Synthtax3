using Microsoft.Win32;
using CommunityToolkit.Mvvm.Input;
using Synthtax.Core.Interfaces;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public enum SolutionInputMode
{
    SolutionFile,
    Folder,
}

public abstract partial class AnalysisViewModelBase : ViewModelBase
{
    private string _inputPath = string.Empty;

    protected SolutionInputMode InputMode { get; init; } = SolutionInputMode.SolutionFile;

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

    // Convenience aliases so XAML bindings and different ViewModels
    // can use whichever name makes most sense for them.
    public string SolutionPath
    {
        get => _inputPath;
        set => InputPath = value;
    }

    public string RepositoryPath
    {
        get => _inputPath;
        set => InputPath = value;
    }

    public bool IsRemoteUrl => IRepositoryResolver.IsRemoteUrl(_inputPath);

    public string PathTypeHint =>
        string.IsNullOrWhiteSpace(_inputPath) ? string.Empty
        : IsRemoteUrl ? "GitHub / GitLab URL"
        : InputMode == SolutionInputMode.Folder ? "Lokal katalog"
                                                : "Lokal .sln-fil";

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
                Title = "Välj .sln-fil eller mapp",
                Filter = "Solution files (*.sln)|*.sln|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
                InputPath = dlg.FileName;
        }
    }

    protected AnalysisViewModelBase(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }

    protected virtual void OnInputPathChanged(string newPath) { }

    /// <summary>
    /// Validates the current InputPath before starting an analysis.
    /// Returns false and sets <paramref name="errorMessage"/> if invalid.
    ///
    /// The API's RepositoryResolverService accepts:
    ///   • A local path to a .sln file
    ///   • A local path to a folder (it searches recursively for a .sln)
    ///   • A remote GitHub/GitLab HTTPS or SSH URL
    /// So the WPF client does NOT need to find the .sln itself — it just
    /// validates that the folder or file actually exists on disk.
    /// </summary>
    protected bool ValidateInputPath(out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(_inputPath))
        {
            errorMessage = InputMode == SolutionInputMode.Folder
                ? "Ange en Git-URL eller lokal sökväg"
                : "Ange en .sln-sökväg, mappväg eller GitHub-URL";
            return false;
        }

        // ── Remote URL: basic format check only ──────────────────────────────
        if (IsRemoteUrl)
        {
            if (!_inputPath.Contains("://") && !_inputPath.StartsWith("git@"))
            {
                errorMessage = "GitHub-länken verkar inte vara en giltig URL. Använd t.ex. https://github.com/user/repo";
                return false;
            }
            // URL validation is done server-side; don't block the user here.
            return true;
        }

        // ── Local path ────────────────────────────────────────────────────────
        var normalized = _inputPath.Trim()
            .TrimEnd(System.IO.Path.DirectorySeparatorChar,
                     System.IO.Path.AltDirectorySeparatorChar);

        if (InputMode == SolutionInputMode.Folder)
        {
            if (!System.IO.Directory.Exists(normalized))
            {
                errorMessage = $"Katalogen existerar inte: {normalized}";
                return false;
            }

            // BUG FIX: .git is a DIRECTORY, not a file.
            // The original code used File.Exists(Path.Combine(path, ".git"))
            // which always returns false for valid Git repos because .git is
            // a directory. This check prevented the user from ever analyzing
            // a local Git repo. Switched to Directory.Exists.
            var gitDir = System.IO.Path.Combine(normalized, ".git");
            if (!System.IO.Directory.Exists(gitDir))
            {
                errorMessage = "Det verkar inte vara ett Git-repository (ingen .git-mapp funnen)";
                return false;
            }
        }
        else
        {
            // SolutionFile mode: accept both a direct .sln path AND a folder
            // (the server searches folders recursively — just check existence).
            if (System.IO.File.Exists(normalized))
            {
                if (!normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Välj en .sln-fil";
                    return false;
                }
            }
            else if (System.IO.Directory.Exists(normalized))
            {
                // Folder path — the server will find the .sln for us.
                // No extra check needed here.
            }
            else
            {
                errorMessage = $"Sökvägen existerar inte: {normalized}";
                return false;
            }
        }

        return true;
    }
}
