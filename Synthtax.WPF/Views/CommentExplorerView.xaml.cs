using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class CommentExplorerView : UserControl
{
    public CommentExplorerView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<CommentExplorerViewModel>();
    }
}
