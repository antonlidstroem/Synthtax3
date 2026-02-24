using System.Windows;
using System.Windows.Controls;

namespace Synthtax.WPF.Controls;

/// <summary>
/// A StackPanel that automatically applies uniform spacing between children.
/// </summary>
public class SpacedStackPanel : StackPanel
{
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(SpacedStackPanel),
            new PropertyMetadata(0.0, OnSpacingChanged));

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SpacedStackPanel panel)
            panel.UpdateChildSpacing();
    }

    protected override void OnVisualChildrenChanged(
        DependencyObject visualAdded, DependencyObject visualRemoved)
    {
        base.OnVisualChildrenChanged(visualAdded, visualRemoved);
        UpdateChildSpacing();
    }

    private void UpdateChildSpacing()
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (Children[i] is not FrameworkElement fe) continue;

            if (Orientation == Orientation.Horizontal)
                fe.Margin = i == 0 ? new Thickness(0) : new Thickness(Spacing, 0, 0, 0);
            else
                fe.Margin = i == 0 ? new Thickness(0) : new Thickness(0, Spacing, 0, 0);
        }
    }
}