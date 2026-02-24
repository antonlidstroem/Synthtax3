using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class MethodExplorerView : UserControl
{
    public MethodExplorerView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<MethodExplorerViewModel>();
    }
}
