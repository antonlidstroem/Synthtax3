using System.Windows.Controls;

namespace Synthtax.WPF.Controls;

/// <summary>
/// Delad inmatningskontroll: sökväg/URL-fält + Bläddra + Analysera.
/// Bindas mot AnalysisViewModelBase (eller subklass) via DataContext.
/// </summary>
public partial class SolutionInputBar : UserControl
{
    public SolutionInputBar()
    {
        InitializeComponent();
    }
}
