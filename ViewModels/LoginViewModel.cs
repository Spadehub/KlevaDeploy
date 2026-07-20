using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.ViewModels;

public sealed class LoginViewModel : ObservableObject
{
    private readonly IAuthService _authService;
    private readonly IPreferencesService _preferencesService;
    private CancellationTokenSource? _loginCts;

    public ObservableCollection<PortalItemViewModel> Portals { get; } = new();

    private PortalItemViewModel? _selectedPortal;
    public PortalItemViewModel? SelectedPortal
    {
        get => _selectedPortal;
        set
        {
            if (!SetProperty(ref _selectedPortal, value)) return;
            SyncSelectedPortalFieldsFromModel();
            PersistPortalSelection();
            OnPropertyChanged(nameof(IsSelectedPortalLoggedIn));
            OnPropertyChanged(nameof(LoggedInAsText));
        }
    }

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

    private bool _isPasswordVisible;
    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set => SetProperty(ref _isPasswordVisible, value);
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
            LogoutCommand.NotifyCanExecuteChanged();
        }
    }

    public bool LoginSucceeded { get; private set; }

    public bool IsSelectedPortalLoggedIn =>
        SelectedPortal is not null && _authService.IsAuthenticatedForPortalHomeUrl(SelectedPortal.HomeUrl);

    public string LoggedInAsText
    {
        get
        {
            var u = (SelectedPortal?.LastUsername ?? Username ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(u) ? "Logged in" : $"Logged in as {u}";
        }
    }

    public IAsyncRelayCommand LoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand GestisciPortaliCommand { get; }
    public IRelayCommand ModificaPortaleCommand { get; }
    public IRelayCommand EliminaPortaleCommand { get; }

    public SettingsSection? RequestedSettingsSection { get; private set; }
    public string? RequestedPortalId { get; private set; }

    public LoginViewModel(IAuthService authService, IPreferencesService preferencesService)
    {
        _authService = authService;
        _preferencesService = preferencesService;

        LoginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
        LogoutCommand = new RelayCommand(Logout, CanLogout);
        CancelCommand = new RelayCommand(Cancel);
        GestisciPortaliCommand = new RelayCommand(GestisciPortali);
        ModificaPortaleCommand = new RelayCommand(ModificaPortale, CanEditOrDeletePortal);
        EliminaPortaleCommand = new RelayCommand(EliminaPortale, CanDeletePortal);

        LoadPortalsFromPreferences();
    }

    private async Task LoginAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            _loginCts?.Dispose();
            _loginCts = new CancellationTokenSource();

            var portalUrl = SelectedPortal?.HomeUrl ?? string.Empty;
            var success = await _authService.LoginAsync(Username, Password, NormalizePortalHomeUrl(portalUrl), _loginCts.Token);
            if (!success)
            {
                ErrorMessage = "Credenziali non valide. Riprovare.";
                return;
            }

            PersistLoginSuccess();
            LoginSucceeded = true;
            OnPropertyChanged(nameof(IsSelectedPortalLoggedIn));
            OnPropertyChanged(nameof(LoggedInAsText));
            LoginCommand.NotifyCanExecuteChanged();
            LogoutCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Accesso annullato.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin()
    {
        if (IsBusy) return false;
        if (SelectedPortal is null) return false;
        if (string.IsNullOrWhiteSpace(Username)) return false;
        if (string.IsNullOrWhiteSpace(Password)) return false;
        var raw = (SelectedPortal.HomeUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return Uri.TryCreate(NormalizePortalHomeUrl(raw), UriKind.Absolute, out _);
    }

    private bool CanLogout() => !IsBusy && IsSelectedPortalLoggedIn;

    private void Logout()
    {
        if (SelectedPortal is not null)
            _authService.LogoutPortal(SelectedPortal.HomeUrl);
        OnPropertyChanged(nameof(IsSelectedPortalLoggedIn));
        OnPropertyChanged(nameof(LoggedInAsText));
        LoginCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
    }

    private void GestisciPortali()
    {
        RequestedSettingsSection = SettingsSection.Portali;
        RequestedPortalId = SelectedPortal?.Id;
        LoginSucceeded = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CanEditOrDeletePortal()
    {
        if (IsBusy) return false;
        if (SelectedPortal is null) return false;
        return true;
    }

    private bool CanDeletePortal()
    {
        if (!CanEditOrDeletePortal()) return false;
        return Portals.Count > 1;
    }

    private void ModificaPortale()
    {
        if (SelectedPortal is not null)
            _authService.LogoutPortal(SelectedPortal.HomeUrl);
        RequestedSettingsSection = SettingsSection.Portali;
        RequestedPortalId = SelectedPortal?.Id;
        LoginSucceeded = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EliminaPortale()
    {
        if (SelectedPortal is null) return;
        if (Portals.Count <= 1) return;

        _authService.LogoutPortal(SelectedPortal.HomeUrl);

        var toRemove = SelectedPortal;
        var removeId = toRemove.Id;

        var idx = Portals.IndexOf(toRemove);
        Portals.Remove(toRemove);

        SelectedPortal = Portals.Count == 0
            ? null
            : Portals[Math.Clamp(idx - 1, 0, Portals.Count - 1)];

        var prefs = _preferencesService.Preferences;
        prefs.Portals.RemoveAll(p => string.Equals(p.Id, removeId, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(prefs.SelectedPortalId, removeId, StringComparison.OrdinalIgnoreCase))
            prefs.SelectedPortalId = SelectedPortal?.Id;

        prefs.SelectedPortalHomeUrl = SelectedPortal?.HomeUrl ?? prefs.SelectedPortalHomeUrl;
        _preferencesService.Save();

        ModificaPortaleCommand.NotifyCanExecuteChanged();
        EliminaPortaleCommand.NotifyCanExecuteChanged();
    }

    private void LoadPortalsFromPreferences()
    {
        var prefs = _preferencesService.Preferences;
        prefs.Portals ??= new();

        Portals.Clear();
        foreach (var portal in prefs.Portals.Where(p => p is not null))
        {
            Portals.Add(new PortalItemViewModel
            {
                Id = portal.Id,
                Name = portal.Name,
                HomeUrl = portal.HomeUrl,
                LastUsername = portal.LastUsername
            });
        }

        var selected = Portals.FirstOrDefault(p => string.Equals(p.Id, prefs.SelectedPortalId, StringComparison.OrdinalIgnoreCase));
        SelectedPortal = selected ?? Portals.FirstOrDefault();
        SyncSelectedPortalFieldsFromModel();
    }

    private void SyncSelectedPortalFieldsFromModel()
    {
        var p = SelectedPortal;
        Username = p?.LastUsername ?? string.Empty;
        Password = string.Empty;
        IsPasswordVisible = false;
        ErrorMessage = string.Empty;
        LoginCommand.NotifyCanExecuteChanged();
        LogoutCommand.NotifyCanExecuteChanged();
        ModificaPortaleCommand.NotifyCanExecuteChanged();
        EliminaPortaleCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(LoggedInAsText));
    }

    private void PersistPortalSelection()
    {
        var prefs = _preferencesService.Preferences;
        prefs.SelectedPortalId = SelectedPortal?.Id;
        _preferencesService.Save();
    }

    private void PersistLoginSuccess()
    {
        if (SelectedPortal is null) return;

        var normalized = NormalizePortalHomeUrl(SelectedPortal.HomeUrl);
        SelectedPortal.HomeUrl = normalized;
        SelectedPortal.LastUsername = Username.Trim();

        var prefs = _preferencesService.Preferences;
        prefs.SelectedPortalId = SelectedPortal.Id;
        prefs.SelectedPortalHomeUrl = normalized;
        prefs.RecentPortalHomeUrls ??= new();
        prefs.RecentPortalHomeUrls.RemoveAll(p => string.Equals((p ?? string.Empty).Trim(), normalized, StringComparison.OrdinalIgnoreCase));
        prefs.RecentPortalHomeUrls.Insert(0, normalized);
        if (prefs.RecentPortalHomeUrls.Count > 10)
            prefs.RecentPortalHomeUrls.RemoveRange(10, prefs.RecentPortalHomeUrls.Count - 10);

        var portal = prefs.Portals.FirstOrDefault(p => string.Equals(p.Id, SelectedPortal.Id, StringComparison.OrdinalIgnoreCase));
        if (portal is not null)
        {
            portal.HomeUrl = normalized;
            portal.LastUsername = Username.Trim();
        }

        _preferencesService.Save();
    }

    private static string NormalizePortalHomeUrl(string? portalHomeUrl)
    {
        var raw = (portalHomeUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return "https://download.passepartout.cloud/.";
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return raw;

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        if (string.Equals(builder.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase))
        {
            var path = (builder.Path ?? string.Empty).TrimEnd('/');
            builder.Path = string.IsNullOrWhiteSpace(path) ? "/." : path + "/.";
        }
        else
        {
            if (!builder.Path.EndsWith("/", StringComparison.OrdinalIgnoreCase))
                builder.Path = builder.Path + "/";
        }

        return builder.Uri.ToString();
    }

    private void Cancel()
    {
        _loginCts?.Cancel();
        LoginSucceeded = false;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CloseRequested;
}

public sealed class PortalItemViewModel : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    private string _name = "Portale";
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _homeUrl = "https://download.passepartout.cloud/.";
    public string HomeUrl
    {
        get => _homeUrl;
        set => SetProperty(ref _homeUrl, value);
    }

    private string? _lastUsername;
    public string? LastUsername
    {
        get => _lastUsername;
        set => SetProperty(ref _lastUsername, value);
    }
}
