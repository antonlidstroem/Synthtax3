using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class CodeAnalysisView : UserControl
{
    public CodeAnalysisView(IServiceProvider services)
    {
        InitializeComponent();
        DataContext = services.GetRequiredService<CodeAnalysisViewModel>();
    }
}
