using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.Core.DTOs;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class BacklogView : UserControl
{
    private readonly BacklogViewModel _vm;

    public BacklogView(IServiceProvider services)
    {
        InitializeComponent();
        _vm = services.GetRequiredService<BacklogViewModel>();
        DataContext = _vm;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.EditItemCommand.CanExecute(_vm.SelectedItem))
            _vm.EditItemCommand.Execute(_vm.SelectedItem);
    }
}
