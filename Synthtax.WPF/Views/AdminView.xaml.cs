using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class AdminView : UserControl
{
    public AdminView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<AdminViewModel>();
    }
}
