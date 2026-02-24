using System.Windows;
using System.Windows.Controls;

namespace Synthtax.WPF.Controls;

/// <summary>
/// A TextBox with placeholder/hint text support.
/// </summary>
public class HintTextBox : TextBox
{
    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(
            nameof(Hint), typeof(string), typeof(HintTextBox),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsHintVisibleProperty =
        DependencyProperty.Register(
            nameof(IsHintVisible), typeof(bool), typeof(HintTextBox),
            new PropertyMetadata(true));

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public bool IsHintVisible
    {
        get => (bool)GetValue(IsHintVisibleProperty);
        private set => SetValue(IsHintVisibleProperty, value);
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        IsHintVisible = string.IsNullOrEmpty(Text);
    }
}