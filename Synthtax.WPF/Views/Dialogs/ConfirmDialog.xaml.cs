using System.Windows;
using System.Windows.Input;

namespace Synthtax.WPF.Views.Dialogs;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message,
        string confirmLabel = "Bekräfta", string cancelLabel = "Avbryt",
        bool isDangerous = false, Window? owner = null)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = new ConfirmDialogModel
        {
            Title        = title,
            Message      = message,
            ConfirmLabel = confirmLabel,
            CancelLabel  = cancelLabel,
            IsDangerous  = isDangerous,
        };
    }

    private void OnConfirm(object sender, RoutedEventArgs e) { DialogResult = true;  Close(); }
    private void OnCancel (object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}

public class ConfirmDialogModel
{
    public string Title        { get; init; } = string.Empty;
    public string Message      { get; init; } = string.Empty;
    public string ConfirmLabel { get; init; } = "Bekräfta";
    public string CancelLabel  { get; init; } = "Avbryt";
    public bool   IsDangerous  { get; init; }
}
