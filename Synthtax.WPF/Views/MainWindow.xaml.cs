using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        _vm = services.GetRequiredService<MainViewModel>();
        DataContext = _vm;

        // Expose service provider for nav commands
        _vm.Initialize(services);
    }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();
}
