using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class GitView : UserControl
{
    public GitView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<GitViewModel>();
    }
}
