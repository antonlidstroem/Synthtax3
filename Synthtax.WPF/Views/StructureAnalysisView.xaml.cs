using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class StructureAnalysisView : UserControl
{
    private readonly StructureAnalysisViewModel _vm;

    public StructureAnalysisView(IServiceProvider services)
    {
        InitializeComponent();
        _vm = services.GetRequiredService<StructureAnalysisViewModel>();
        DataContext = _vm;
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is StructureTreeNode node)
            _vm.SelectedNode = node;
    }
}
