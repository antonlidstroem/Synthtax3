using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class SecurityView : UserControl
{
    public SecurityView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<SecurityViewModel>();
    }
}
