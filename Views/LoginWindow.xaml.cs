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

        PasswordBox.PasswordChanged += (_, _) =>
        {
            _vm.Password = PasswordBox.Password;
        };

        vm.CloseRequested += (_, _) => DialogResult = vm.LoginSucceeded;

        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };
    }
}
