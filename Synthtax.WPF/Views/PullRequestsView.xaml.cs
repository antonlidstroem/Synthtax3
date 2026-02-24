using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class PullRequestsView : UserControl
{
    public PullRequestsView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<PullRequestsViewModel>();
    }
}
