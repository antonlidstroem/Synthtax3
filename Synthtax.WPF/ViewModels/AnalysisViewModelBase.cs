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
                Title = "Välj .sln-fil",
                Filter = "Solution files (*.sln)|*.sln",
            };
            if (dlg.ShowDialog() == true)
                InputPath = dlg.FileName;
        }
    }
    protected AnalysisViewModelBase(ApiClient api, TokenStore tokenStore)
        : base(api, tokenStore) { }
    protected virtual void OnInputPathChanged(string newPath) { }

    /// <summary>
    /// IMPROVED: Validera input innan analys startar.
    /// Detekterar URL vs lokal sökväg och visar riktig felmeddelande.
    /// </summary>
    protected bool ValidateInputPath(out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(_inputPath))
        {
            errorMessage = InputMode == SolutionInputMode.Folder
                ? "Ange en Git-URL eller lokal sökväg"
                : "Ange en .sln-fil sökväg eller URL";
            return false;
        }

        // Detektera URL
        if (IsRemoteUrl)
        {
            // Validera GitHub/GitLab URL format
            if (!_inputPath.Contains("://"))
            {
                errorMessage = "GitHub-länken verkar inte vara en giltig URL. Använd t.ex. https://github.com/user/repo";
                return false;
            }
        }
        else
        {
            // Validera lokal sökväg
            if (InputMode == SolutionInputMode.Folder)
            {
                if (!System.IO.Directory.Exists(_inputPath))
                {
                    errorMessage = $"Katalogen existerar inte: {_inputPath}";
                    return false;
                }
                if (!System.IO.File.Exists(System.IO.Path.Combine(_inputPath, ".git")))
                {
                    errorMessage = "Det verkar inte vara ett Git-repository (ingen .git-mapp funnen)";
                    return false;
                }
            }
            else
            {
                if (!System.IO.File.Exists(_inputPath))
                {
                    errorMessage = $"Filen existerar inte: {_inputPath}";
                    return false;
                }
                if (!_inputPath.EndsWith(".sln"))
                {
                    errorMessage = "Välj en .sln-fil";
                    return false;
                }
            }
        }

        return true;
    }
}
