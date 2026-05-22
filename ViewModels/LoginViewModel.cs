using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _isBusy;

    public bool LoginSucceeded { get; private set; }

    public LoginViewModel(IAuthService authService) => _authService = authService;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var success = await _authService.LoginAsync(Username, Password);
            if (success)
            {
                LoginSucceeded = true;
                // Signal the window to close — handled via CloseRequested event
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = "Credenziali non valide. Riprovare.";
            }
        }
        finally { IsBusy = false; }
    }

    private bool CanLogin() => !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Raised when the VM wants the LoginWindow to close.</summary>
    public event EventHandler? CloseRequested;

    partial void OnUsernameChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => LoginCommand.NotifyCanExecuteChanged();
    partial void OnIsBusyChanged(bool value) => LoginCommand.NotifyCanExecuteChanged();
}
