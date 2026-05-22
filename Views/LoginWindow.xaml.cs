using System.Windows;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Wire password box (PasswordBox cannot bind directly for security reasons)
        PasswordBox.PasswordChanged += (_, _) =>
        {
            _vm.Password = PasswordBox.Password;
        };

        // Close window when VM signals success
        vm.CloseRequested += (_, _) => DialogResult = true;
    }
}
