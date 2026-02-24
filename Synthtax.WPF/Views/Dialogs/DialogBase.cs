using System.Windows;
using System.Windows.Input;

namespace Synthtax.WPF.Views.Dialogs;

/// <summary>Base class for all modal dialogs in Synthtax.</summary>
public class DialogBase : Window
{
    protected DialogBase()
    {
        WindowStyle             = WindowStyle.None;
        AllowsTransparency      = true;
        Background              = System.Windows.Media.Brushes.Transparent;
        WindowStartupLocation   = WindowStartupLocation.CenterOwner;
        ShowInTaskbar           = false;
        ResizeMode              = ResizeMode.NoResize;

        // ESC closes
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    protected void OnClose(object sender, RoutedEventArgs e)
        => Close();

    protected void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
