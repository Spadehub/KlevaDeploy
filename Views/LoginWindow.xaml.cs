using System.ComponentModel;
using System.Windows;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm;
    private bool _syncingPassword;

    public LoginWindow(LoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        PasswordBox.PasswordChanged += (_, _) =>
        {
            if (_syncingPassword) return;
            _vm.Password = PasswordBox.Password;
        };

        _vm.PropertyChanged += OnViewModelPropertyChanged;

        vm.CloseRequested += (_, _) => DialogResult = vm.LoginSucceeded;

        TitleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 1) DragMove();
        };
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _vm.IsPasswordVisible = !_vm.IsPasswordVisible;
    }

    private void OnPasswordVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncPasswordBoxFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.Password))
            SyncPasswordBoxFromViewModel();
    }

    private void SyncPasswordBoxFromViewModel()
    {
        if (PasswordBox.Password == _vm.Password)
            return;

        _syncingPassword = true;
        try
        {
            PasswordBox.Password = _vm.Password ?? string.Empty;
        }
        finally
        {
            _syncingPassword = false;
        }
    }
}
