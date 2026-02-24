using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class AIDetectionView : UserControl
{
    public AIDetectionView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<AIDetectionViewModel>();
    }
}
