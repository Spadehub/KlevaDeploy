using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Models;
using DeploymentApp.Services.Interfaces;
using DeploymentApp.Views;

namespace DeploymentApp.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IInstallerService _installerService;
    private readonly IUpdateService _updateService;
    private readonly IAuthService _authService;
    private readonly ILogService _log;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly Func<LoginViewModel> _loginVmFactory;

    private IReadOnlyList<DeploymentProcess> _allProcesses = Array.Empty<DeploymentProcess>();

    public ObservableCollection<PresetViewModel> Presets { get; } = new();
    public ObservableCollection<ProcessStepViewModel> ExecutionQueue { get; } = new();

    [ObservableProperty] private bool _isInitializing;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _overallStatus = "Pronto";
    [ObservableProperty] private int _selectedPresetCount;
    [ObservableProperty] private string _themeToggleTooltip = "Passa al tema chiaro";

    public LogViewModel LogViewModel { get; }

    public MainViewModel(
        IInstallerService installerService,
        IUpdateService updateService,
        IAuthService authService,
        ILogService log,
        IThemeService themeService,
        IDialogService dialogService,
        Func<LoginViewModel> loginVmFactory,
        LogViewModel logViewModel)
    {
        _installerService = installerService;
        _updateService = updateService;
        _authService = authService;
        _log = log;
        _themeService = themeService;
        _dialogService = dialogService;
        _loginVmFactory = loginVmFactory;
        LogViewModel = logViewModel;

        SyncThemeProperties();
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        IsInitializing = true;
        try
        {
            _allProcesses = await _installerService.LoadProcessesAsync();
            var presets = await _installerService.LoadPresetsAsync();
            foreach (var p in presets)
            {
                var vm = new PresetViewModel(p);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PresetViewModel.IsSelected))
                        RebuildExecutionQueue();
                };
                Presets.Add(vm);
            }
            _log.Info($"Loaded {presets.Count} presets and {_allProcesses.Count} processes.");

            // Background update check — fire and forget, errors are logged internally
            var packageList = _allProcesses
                .Select(p => new SoftwarePackage
                {
                    Id = p.Id,
                    Name = p.Name,
                    LocalInstallerRelativePath = p.RelativePath,
                    DownloadUrl = p.DownloadUrl,
                    RequiresAuth = p.RequiresAuth
                })
                .ToList();
            _ = Task.Run(() => _updateService.CheckAndUpdateAsync(packageList));
        }
        finally { IsInitializing = false; }
    }

    private void RebuildExecutionQueue()
    {
        var selected = Presets.Where(p => p.IsSelected).Select(p => p.Preset).ToList();
        SelectedPresetCount = selected.Count;
        ExecutionQueue.Clear();
        if (selected.Count == 0) return;
        var queue = _installerService.BuildExecutionQueue(selected, _allProcesses);
        foreach (var (process, order) in queue)
            ExecutionQueue.Add(new ProcessStepViewModel(process, order, _dialogService));
        _log.Info($"Execution queue rebuilt: {ExecutionQueue.Count} steps from {selected.Count} preset(s).");
        RunQueueCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunQueue))]
    private async Task RunQueueAsync()
    {
        var enabledSteps = ExecutionQueue.Where(s => s.IsEnabled).ToList();
        if (enabledSteps.Count == 0) return;

        // Check if any step requires auth
        if (enabledSteps.Any(s => s.Process.RequiresAuth) && !_authService.IsAuthenticated)
        {
            OpenLogin();
            if (!_authService.IsAuthenticated) return;
        }

        IsRunning = true;
        OverallStatus = "Esecuzione in corso...";
        RunQueueCommand.NotifyCanExecuteChanged();

        try
        {
            foreach (var step in enabledSteps)
            {
                step.SetStatus("▶️", "In esecuzione...");
                _log.Info($"[{step.Order}] Starting: {step.Name}");
                // Actual execution is handled by PackageDetailViewModel / ProcessExecutionService
                // Here we simulate for demo — replace with real call
                await Task.Delay(800); // Demo delay
                step.SetStatus("✅", "Completato");
                _log.Info($"[{step.Order}] Completed: {step.Name}");
            }
            OverallStatus = $"Completato — {enabledSteps.Count} step eseguiti.";
        }
        catch (Exception ex)
        {
            _log.Error("Queue execution failed", ex);
            OverallStatus = $"Errore: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            RunQueueCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunQueue() => ExecutionQueue.Count > 0 && !IsRunning && !IsInitializing;

    partial void OnIsRunningChanged(bool value) => RunQueueCommand.NotifyCanExecuteChanged();
    partial void OnIsInitializingChanged(bool value) => RunQueueCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void OpenLogin()
    {
        if (_authService.IsAuthenticated) { IsAuthenticated = true; return; }
        var vm = _loginVmFactory();
        var win = new LoginWindow(vm);
        win.ShowDialog();
        IsAuthenticated = _authService.IsAuthenticated;
    }

    [RelayCommand]
    private void Logout()
    {
        _authService.Logout();
        IsAuthenticated = false;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
        SyncThemeProperties();
    }

    private void SyncThemeProperties()
    {
        ThemeToggleTooltip = _themeService.CurrentTheme == AppTheme.Dark
            ? "Passa al tema chiaro"
            : "Passa al tema scuro";
    }
}
