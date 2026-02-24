using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Synthtax.WPF.Services;

public class NavigationService : ObservableObject
{
    private UserControl? _currentView;
    private string _currentModule = string.Empty;

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public string CurrentModule
    {
        get => _currentModule;
        private set => SetProperty(ref _currentModule, value);
    }

    public event EventHandler<string>? Navigated;

    public void NavigateTo(UserControl view, string moduleName)
    {
        CurrentView = view;
        CurrentModule = moduleName;
        Navigated?.Invoke(this, moduleName);
    }
}
