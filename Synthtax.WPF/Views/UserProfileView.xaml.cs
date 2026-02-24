using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Synthtax.WPF.ViewModels;

namespace Synthtax.WPF.Views;

public partial class UserProfileView : UserControl
{
    private readonly UserProfileViewModel _vm;

    public UserProfileView(IServiceProvider services)
    {
        InitializeComponent();
        _vm = services.GetRequiredService<UserProfileViewModel>();
        DataContext = _vm;

        CurrentPwBox.PasswordChanged += (_, _) => _vm.CurrentPassword = CurrentPwBox.Password;
        NewPwBox.PasswordChanged     += (_, _) => _vm.NewPassword     = NewPwBox.Password;
        ConfirmPwBox.PasswordChanged += (_, _) => _vm.ConfirmPassword = ConfirmPwBox.Password;
    }
}
