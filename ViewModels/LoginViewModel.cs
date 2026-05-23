using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;

    private string _username = string.Empty;
    public string Username
    {
        get => _username;
        set
        {
            if (!SetProperty(ref _username, value)) return;
            LoginCommand.NotifyCanExecuteChanged();
        }
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set
        {
            if (!SetProperty(ref _password, value)) return;
            LoginCommand.NotifyCanExecuteChanged();
        }
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            LoginCommand.NotifyCanExecuteChanged();
        }
    }

    public bool LoginSucceeded { get; private set; }

    public IAsyncRelayCommand LoginCommand { get; }

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
    }

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
}
