using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private CancellationTokenSource? _loginCts;

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
    public IRelayCommand CancelCommand { get; }

    public LoginViewModel(IAuthService authService)
    {
        _authService = authService;
        LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        CancelCommand = new RelayCommand(Cancel);
    }

    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            _loginCts?.Dispose();
            _loginCts = new CancellationTokenSource();

            var success = await _authService.LoginAsync(Username, Password, _loginCts.Token);
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
        catch (OperationCanceledException)
        {
            ErrorMessage = "Accesso annullato.";
        }
        finally { IsBusy = false; }
    }

    private bool CanLogin() => !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    private void Cancel()
    {
        _loginCts?.Cancel();
        LoginSucceeded = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the VM wants the LoginWindow to close.</summary>
    public event EventHandler? CloseRequested;
}
