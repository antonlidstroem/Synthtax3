using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class MetricsView : UserControl
{
    public MetricsView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<MetricsViewModel>();
    }

    private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
    {

    }
}
