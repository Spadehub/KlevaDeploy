using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.Views;

namespace KlevaDeploy.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IInstallerService _installerService;
    private readonly IUpdateService _updateService;
    private readonly IAuthService _authService;
    private readonly IDownloadDirectoryListingService _downloadDirectoryListingService;
    private readonly IAppUpdateService _appUpdateService;
    private readonly IProcessExecutionService _processExecutionService;
    private readonly ILicenseScraperService _licenseScraperService;
    private readonly ILogService _log;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogService;
    private readonly IPresetIconService _presetIconService;
    private readonly IPreferencesService _prefsService;
    private readonly Func<LoginViewModel> _loginVmFactory;

    private IReadOnlyList<DeploymentProcess> _allProcesses = Array.Empty<DeploymentProcess>();
    private readonly List<DeploymentProcess> _userCreatedProcesses = new();
    private IReadOnlyList<DeploymentPreset> _allPresets = Array.Empty<DeploymentPreset>();
    private readonly Dictionary<string, bool> _userManualDeselections = new();

    private System.Threading.CancellationTokenSource? _queueCts;
    private ProcessStepViewModel? _currentRunningStep;

    public ObservableCollection<PresetViewModel> Presets { get; } = new();
    public ObservableCollection<PresetViewModel> FilteredPresets { get; } = new();
    public ObservableCollection<ProcessStepViewModel> ExecutionQueue { get; } = new();
    public ObservableCollection<ProcessStepViewModel> FilteredExecutionQueue { get; } = new();
    public ObservableCollection<object> FilteredExecutionQueueDisplayItems { get; } = new();

    private bool _suppressQueueResort;

    private bool _isInitializing;
    public bool IsInitializing
    {
        get => _isInitializing;
        set
        {
            if (!SetProperty(ref _isInitializing, value)) return;
            RunQueueCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isAuthenticated;
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set
        {
            if (!SetProperty(ref _isAuthenticated, value)) return;
            OnPropertyChanged(nameof(AuthenticatedPortalsCount));
        }
    }

    public int AuthenticatedPortalsCount => _authService.AuthenticatedPortalCount;

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            RunQueueCommand.NotifyCanExecuteChanged();
            CancelQueueCommand.NotifyCanExecuteChanged();
            CancelStepCommand.NotifyCanExecuteChanged();
        }
    }

    private string _overallStatus = "Pronto";
    public string OverallStatus
    {
        get => _overallStatus;
        set => SetProperty(ref _overallStatus, value);
    }

    private int _selectedPresetCount;
    public int SelectedPresetCount
    {
        get => _selectedPresetCount;
        set => SetProperty(ref _selectedPresetCount, value);
    }

    private string _themeToggleTooltip = "Passa al tema chiaro";
    public string ThemeToggleTooltip
    {
        get => _themeToggleTooltip;
        set => SetProperty(ref _themeToggleTooltip, value);
    }

    private bool _isDarkTheme = true;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => SetProperty(ref _isDarkTheme, value);
    }

    private bool _isDemoMode = true;
    public bool IsDemoMode
    {
        get => _isDemoMode;
        set
        {
            if (!SetProperty(ref _isDemoMode, value)) return;
            _log.Info($"Demo mode changed to: {value}");
            _ = LoadDataAsync();
        }
    }

    private string _presetSearchText = string.Empty;
    public string PresetSearchText
    {
        get => _presetSearchText;
        set
        {
            if (!SetProperty(ref _presetSearchText, value)) return;
            ApplyPresetFilter();
        }
    }

    private string _processSearchText = string.Empty;
    public string ProcessSearchText
    {
        get => _processSearchText;
        set
        {
            if (!SetProperty(ref _processSearchText, value)) return;
            ApplyProcessFilter();
        }
    }

    private bool _isCreatePresetPanelOpen;
    public bool IsCreatePresetPanelOpen
    {
        get => _isCreatePresetPanelOpen;
        set => SetProperty(ref _isCreatePresetPanelOpen, value);
    }

    private bool _isCreateProcessPanelOpen;
    public bool IsCreateProcessPanelOpen
    {
        get => _isCreateProcessPanelOpen;
        set => SetProperty(ref _isCreateProcessPanelOpen, value);
    }

    private bool _isPresetGridView;
    public bool IsPresetGridView
    {
        get => _isPresetGridView;
        set
        {
            if (!SetProperty(ref _isPresetGridView, value)) return;
            _prefsService.Preferences.PresetsViewMode = value ? PresetsViewMode.Grid : PresetsViewMode.List;
            _prefsService.Save();
        }
    }

    private bool _isTerminalTabSelected;
    public bool IsTerminalTabSelected
    {
        get => _isTerminalTabSelected;
        set => SetProperty(ref _isTerminalTabSelected, value);
    }

    private bool _isAppUpdateAvailable;
    public bool IsAppUpdateAvailable
    {
        get => _isAppUpdateAvailable;
        set
        {
            if (!SetProperty(ref _isAppUpdateAvailable, value)) return;
            DownloadAndRestartForUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private string _availableAppVersion = string.Empty;
    public string AvailableAppVersion
    {
        get => _availableAppVersion;
        set => SetProperty(ref _availableAppVersion, value);
    }

    private bool _isDownloadingAppUpdate;
    public bool IsDownloadingAppUpdate
    {
        get => _isDownloadingAppUpdate;
        set
        {
            if (!SetProperty(ref _isDownloadingAppUpdate, value)) return;
            DownloadAndRestartForUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private AppUpdateInfo? _pendingAppUpdate;
    private string? _downloadedAppUpdatePath;

    public LogViewModel LogViewModel { get; }
    public CreatePresetViewModel CreatePresetViewModel { get; }
    public CreateProcessViewModel CreateProcessViewModel { get; }

    public IAsyncRelayCommand InitializeCommand { get; }
    public IRelayCommand CreateProcessCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> EditProcessCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> DeleteProcessCommand { get; }
    public IRelayCommand OpenCreatePresetCommand { get; }
    public IRelayCommand<PresetViewModel?> EditPresetCommand { get; }
    public IRelayCommand<PresetViewModel?> DeletePresetCommand { get; }
    public IRelayCommand ClearPresetSearchCommand { get; }
    public IRelayCommand ClearProcessSearchCommand { get; }
    public IAsyncRelayCommand RunQueueCommand { get; }
    public IRelayCommand CancelQueueCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> CancelStepCommand { get; }
    public IAsyncRelayCommand<ProcessStepViewModel?> UpdateInstallerCommand { get; }
    public IAsyncRelayCommand<ProcessStepViewModel?> RedownloadInstallerCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> RevealInstallerInExplorerCommand { get; }
    public IAsyncRelayCommand CheckAppUpdateCommand { get; }
    public IAsyncRelayCommand DownloadAndRestartForUpdateCommand { get; }
    public IRelayCommand OpenLoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }
    public IRelayCommand SetPresetListViewCommand { get; }
    public IRelayCommand SetPresetGridViewCommand { get; }

    public MainViewModel(
        IInstallerService installerService,
        IUpdateService updateService,
        IAuthService authService,
        IDownloadDirectoryListingService downloadDirectoryListingService,
        IAppUpdateService appUpdateService,
        IProcessExecutionService processExecutionService,
        ILicenseScraperService licenseScraperService,
        ILogService log,
        IThemeService themeService,
        IDialogService dialogService,
        IPresetIconService presetIconService,
        IPreferencesService prefsService,
        Func<LoginViewModel> loginVmFactory,
        LogViewModel logViewModel)
    {
        _installerService = installerService;
        _updateService = updateService;
        _authService = authService;
        _downloadDirectoryListingService = downloadDirectoryListingService;
        _appUpdateService = appUpdateService;
        _processExecutionService = processExecutionService;
        _licenseScraperService = licenseScraperService;
        _log = log;
        _themeService = themeService;
        _dialogService = dialogService;
        _presetIconService = presetIconService;
        _prefsService = prefsService;
        _loginVmFactory = loginVmFactory;
        LogViewModel = logViewModel;

        // Auto-switch output view to Terminal whenever a process emits output.
        // This makes the live stdout/stderr stream visible even if the user is on the "Log" tab.
        _log.LogAdded += (_, entry) =>
        {
            if (entry.Level is not ("STDOUT" or "STDERR")) return;
            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher is null) { IsTerminalTabSelected = true; return; } // unit tests / non-WPF contexts
            dispatcher.BeginInvoke(() => IsTerminalTabSelected = true);
        };

        CreatePresetViewModel = new CreatePresetViewModel(_presetIconService);
        CreatePresetViewModel.CloseRequested += OnCreatePresetCloseRequested;
        CreatePresetViewModel.DeleteRequested += OnCreatePresetDeleteRequested;

        CreateProcessViewModel = new CreateProcessViewModel(_authService, _downloadDirectoryListingService, _prefsService, _log, _presetIconService, _processExecutionService, _updateService, openLoginAsync: () =>
        {
            OpenLogin();
            return Task.CompletedTask;
        });
        CreateProcessViewModel.DeleteRequested += OnCreateProcessDeleteRequested;
        CreateProcessViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CreateProcessViewModel.DialogResult)) return;
            if (CreateProcessViewModel.DialogResult is null) return;
            OnCreateProcessCloseRequested();
        };

        _authService.AuthStateChanged += (_, _) =>
        {
            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher is null)
                SyncAuthProperties();
            else
                dispatcher.BeginInvoke(SyncAuthProperties);
        };

        SyncThemeProperties();

        IsPresetGridView = _prefsService.Preferences.PresetsViewMode == PresetsViewMode.Grid;
        IsTerminalTabSelected = false;

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        CreateProcessCommand = new RelayCommand(CreateProcess);
        EditProcessCommand = new RelayCommand<ProcessStepViewModel?>(EditProcess);
        DeleteProcessCommand = new RelayCommand<ProcessStepViewModel?>(DeleteProcess);
        OpenCreatePresetCommand = new RelayCommand(OpenCreatePreset);
        EditPresetCommand = new RelayCommand<PresetViewModel?>(EditPreset);
        DeletePresetCommand = new RelayCommand<PresetViewModel?>(DeletePreset);
        ClearPresetSearchCommand = new RelayCommand(ClearPresetSearch);
        ClearProcessSearchCommand = new RelayCommand(ClearProcessSearch);
        RunQueueCommand = new AsyncRelayCommand(RunQueueAsync, CanRunQueue);
        CancelQueueCommand = new RelayCommand(CancelQueue, CanCancelQueue);
        CancelStepCommand = new RelayCommand<ProcessStepViewModel?>(CancelStep, CanCancelStep);
        UpdateInstallerCommand = new AsyncRelayCommand<ProcessStepViewModel?>(UpdateInstallerAsync, CanUpdateInstaller);
        RedownloadInstallerCommand = new AsyncRelayCommand<ProcessStepViewModel?>(RedownloadInstallerAsync, CanRedownloadInstaller);
        RevealInstallerInExplorerCommand = new RelayCommand<ProcessStepViewModel?>(RevealInstallerInExplorer, CanRevealInstallerInExplorer);
        CheckAppUpdateCommand = new AsyncRelayCommand(CheckAppUpdateAsync);
        DownloadAndRestartForUpdateCommand = new AsyncRelayCommand(DownloadAndRestartForUpdateAsync, CanDownloadAndRestartForUpdate);
        OpenLoginCommand = new RelayCommand(OpenLogin);
        LogoutCommand = new RelayCommand(Logout);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        SetPresetListViewCommand = new RelayCommand(() => IsPresetGridView = false);
        SetPresetGridViewCommand = new RelayCommand(() => IsPresetGridView = true);

        ExecutionQueue.CollectionChanged += (_, _) => RunQueueCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        _ = await _authService.TryRestoreSessionAsync();
        SyncAuthProperties();
        await LoadDataAsync();
        // ISSUE 3 FIX: Rebuild execution queue at startup to show all processes
        RebuildExecutionQueue();
        _ = CheckAppUpdateAsync();
    }

    private void SyncAuthProperties()
    {
        IsAuthenticated = _authService.IsAuthenticated;
        OnPropertyChanged(nameof(AuthenticatedPortalsCount));
        RefreshLoginBadges();
    }

    private void RefreshLoginBadges()
    {
        foreach (var step in ExecutionQueue)
        {
            step.ShowLoginBadge = step.Process.RequiresAuth && !IsAuthenticatedForProcess(step.Process);
        }
    }

    private bool IsAuthenticatedForProcess(DeploymentProcess process)
    {
        if (process is null) return false;

        var portalId = (process.PortalId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(portalId))
        {
            var portal = _prefsService.Preferences.Portals
                .FirstOrDefault(p => string.Equals(p.Id, portalId, StringComparison.OrdinalIgnoreCase));
            if (portal is not null && !string.IsNullOrWhiteSpace(portal.HomeUrl))
                return _authService.IsAuthenticatedForPortalHomeUrl(portal.HomeUrl);
        }

        var url = process.InstallerSourceMode == InstallerSourceMode.DynamicWeb
            ? process.DownloadBaseFolderUrl
            : process.DownloadUrl;
        if (!string.IsNullOrWhiteSpace(url))
            return _authService.IsAuthenticatedForUrl(url);

        return _authService.IsAuthenticated;
    }

    private async Task LoadDataAsync()
    {
        IsInitializing = true;
        try
        {
            _allProcesses = await _installerService.LoadProcessesAsync(IsDemoMode);
            _allPresets = await _installerService.LoadPresetsAsync(IsDemoMode);
            
            Presets.Clear();
            foreach (var p in _allPresets)
            {
                var vm = new PresetViewModel(p);
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PresetViewModel.IsSelected))
                        RebuildExecutionQueue();
                };
                Presets.Add(vm);
            }

            ApplyPresetFilter();
            _log.Info($"Loaded {_allPresets.Count} presets and {_allProcesses.Count} processes (Demo Mode: {IsDemoMode}).");

            _ = Task.Run(() => _updateService.CheckAndUpdateInstallersAsync(_allProcesses));
        }
        finally { IsInitializing = false; }
    }

    private void ApplyPresetFilter()
    {
        FilteredPresets.Clear();
        var searchLower = PresetSearchText?.ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(searchLower)
            ? Presets
            : Presets.Where(p =>
                p.Name.ToLowerInvariant().Contains(searchLower) ||
                p.Description.ToLowerInvariant().Contains(searchLower) ||
                p.Category.ToLowerInvariant().Contains(searchLower));

        foreach (var preset in filtered)
        {
            FilteredPresets.Add(preset);
        }
    }

    private void ApplyProcessFilter()
    {
        FilteredExecutionQueue.Clear();
        FilteredExecutionQueueDisplayItems.Clear();
        var searchLower = ProcessSearchText?.ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(searchLower)
            ? ExecutionQueue
            : ExecutionQueue.Where(p => p.Name.ToLowerInvariant().Contains(searchLower));

        var ordered = filtered
            .OrderByDescending(p => p.IsEnabled)
            .ThenBy(p => p.Order)
            .ToList();

        foreach (var process in ordered)
        {
            FilteredExecutionQueue.Add(process);
        }

        var enabled = ordered.Where(p => p.IsEnabled).ToList();
        var disabled = ordered.Where(p => !p.IsEnabled).ToList();
        foreach (var it in enabled) FilteredExecutionQueueDisplayItems.Add(it);
        if (enabled.Count > 0 && disabled.Count > 0) FilteredExecutionQueueDisplayItems.Add(new ListSeparatorItem());
        foreach (var it in disabled) FilteredExecutionQueueDisplayItems.Add(it);
    }

    private void RebuildExecutionQueue()
    {
        var selected = Presets.Where(p => p.IsSelected).Select(p => p.Preset).ToList();
        SelectedPresetCount = selected.Count;
        
        // Store current user manual overrides (only for processes NOT in the selected presets)
        // If a process IS in a selected preset, the preset always wins.
        foreach (var step in ExecutionQueue)
        {
            if (!step.IsInSelectedPreset)
            {
                if (step.IsEnabled)
                    _userManualDeselections[step.Process.Id] = false; // Forced ON by user
                else
                    _userManualDeselections.Remove(step.Process.Id); // Default (OFF)
            }
            else
            {
                // Process is in a selected preset.
                // If user deselected it, we store it as a forced OFF.
                if (!step.IsEnabled)
                    _userManualDeselections[step.Process.Id] = true; // Forced OFF by user
                else
                    _userManualDeselections.Remove(step.Process.Id); // Default (ON)
            }
        }
        
        foreach (var step in ExecutionQueue)
            step.PropertyChanged -= OnExecutionQueueStepPropertyChanged;

        ExecutionQueue.Clear();
        
        // Combine all available processes
        var allAvailableProcesses = _installerService.GetAllAvailableProcesses();
        
        // Determine which processes are in selected presets
        HashSet<string> processesInSelectedPresets = new();
        Dictionary<string, int> processOrderInPreset = new();
        Dictionary<string, bool> processRequiredInPreset = new();
        if (selected.Count > 0)
        {
            var queue = _installerService.BuildExecutionQueue(selected, allAvailableProcesses);
            var order = 10;
            foreach (var item in queue)
            {
                processesInSelectedPresets.Add(item.Process.Id);
                processOrderInPreset[item.Process.Id] = order;
                processRequiredInPreset[item.Process.Id] = item.IsRequired;
                order += 10;
            }
        }
        
        // Always show ALL processes
        var tempList = new List<ProcessStepViewModel>();
        foreach (var process in allAvailableProcesses)
        {
            bool isInSelectedPreset = processesInSelectedPresets.Contains(process.Id);
            int order = isInSelectedPreset ? processOrderInPreset[process.Id] : 9999;
            bool isRequired = isInSelectedPreset && processRequiredInPreset.GetValueOrDefault(process.Id, false);
            var stepVm = new ProcessStepViewModel(process, order, _dialogService, isInSelectedPreset, isRequired);
            stepVm.ShowLoginBadge = stepVm.Process.RequiresAuth && !IsAuthenticatedForProcess(stepVm.Process);
            stepVm.PropertyChanged += OnExecutionQueueStepPropertyChanged;
            
            // Determine enabled state
            if (_userManualDeselections.TryGetValue(process.Id, out bool forcedOff))
            {
                // If forcedOff is true, it means user manually deselected it.
                // If forcedOff is false, it means user manually selected it (even if not in preset).
                stepVm.SetIsEnabledSilently(!forcedOff);
            }
            else
            {
                // Default: ON if in preset, OFF otherwise
                stepVm.SetIsEnabledSilently(isInSelectedPreset);
            }
            
            tempList.Add(stepVm);
        }
        
        // Sort by enabled status first (selected on top), then by order
        var sortedList = tempList
            .OrderByDescending(s => s.IsEnabled)
            .ThenBy(s => s.Order)
            .ToList();
        
        foreach (var step in sortedList)
        {
            ExecutionQueue.Add(step);
        }

        ApplyProcessFilter();
        _log.Info($"Execution queue rebuilt: {ExecutionQueue.Count} total processes ({processesInSelectedPresets.Count} in selected presets).");
    }

    private void OnExecutionQueueStepPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProcessStepViewModel.IsEnabled)) return;
        if (sender is not ProcessStepViewModel step) return;

        if (step.IsInSelectedPreset)
        {
            if (!step.IsEnabled)
                _userManualDeselections[step.Process.Id] = true;
            else
                _userManualDeselections.Remove(step.Process.Id);
        }
        else
        {
            if (step.IsEnabled)
                _userManualDeselections[step.Process.Id] = false;
            else
                _userManualDeselections.Remove(step.Process.Id);
        }

        ResortExecutionQueue();
        ApplyProcessFilter();
    }

    private void ResortExecutionQueue()
    {
        if (_suppressQueueResort) return;
        _suppressQueueResort = true;
        try
        {
            var sorted = ExecutionQueue
                .OrderByDescending(s => s.IsEnabled)
                .ThenBy(s => s.Order)
                .ToList();

            ExecutionQueue.Clear();
            foreach (var step in sorted)
            {
                ExecutionQueue.Add(step);
            }
        }
        finally
        {
            _suppressQueueResort = false;
        }
    }

    private bool CanUpdateInstaller(ProcessStepViewModel? step) =>
        step is not null && step.Process.Kind == ProcessKind.Installer && !IsInitializing;

    private async Task UpdateInstallerAsync(ProcessStepViewModel? step)
    {
        if (step is null) return;

        try
        {
            step.SetStatus("⏳", "Aggiornamento...");
            await _updateService.UpdateSingleInstallerAsync(step.Process);
            step.SetStatus("✅", "Verificato");
        }
        catch (Exception ex)
        {
            _log.Error($"Installer update failed for {step.Process.Name}", ex);
            step.SetStatus("❌", "Errore update");
        }
    }

    private bool CanRedownloadInstaller(ProcessStepViewModel? step) =>
        step is not null &&
        step.Process.Kind == ProcessKind.Installer &&
        step.Process.InstallerSourceMode == InstallerSourceMode.StaticWeb &&
        !string.IsNullOrWhiteSpace(step.Process.DownloadUrl) &&
        _updateService.IsStaticWebInstallerCachedForUrl(step.Process) &&
        !IsInitializing;

    private async Task RedownloadInstallerAsync(ProcessStepViewModel? step)
    {
        if (step is null) return;

        try
        {
            step.SetStatus("⏳", "Download...");
            await _updateService.RedownloadSingleInstallerAsync(step.Process);
            step.SetStatus("✅", "Scaricato");
        }
        catch (Exception ex)
        {
            _log.Error($"Installer redownload failed for {step.Process.Name}", ex);
            step.SetStatus("❌", "Errore download");
        }
    }

    private bool CanRevealInstallerInExplorer(ProcessStepViewModel? step) =>
        step is not null && step.Process.Kind == ProcessKind.Installer;

    private void RevealInstallerInExplorer(ProcessStepViewModel? step)
    {
        if (step is null) return;
        if (string.IsNullOrWhiteSpace(step.Process.RelativePath)) return;

        var path = Path.Combine(AppContext.BaseDirectory, step.Process.RelativePath);
        var args = $"/select,\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    private void CreateProcess()
    {
        try
        {
            IsCreatePresetPanelOpen = false;
            CreateProcessViewModel.InitializeNew();
            IsCreateProcessPanelOpen = true;
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'apertura del pannello creazione processo", ex);
        }
    }

    private void EditProcess(ProcessStepViewModel? stepVm)
    {
        if (stepVm is null) return;
        try
        {
            IsCreatePresetPanelOpen = false;
            CreateProcessViewModel.InitializeForEdit(stepVm.Process);
            IsCreateProcessPanelOpen = true;
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'apertura del pannello modifica processo", ex);
        }
    }

    private void OnCreateProcessCloseRequested()
    {
        try
        {
            var vm = CreateProcessViewModel;
            var isOk = vm.DialogResult == true && vm.CreatedProcess is not null;
            if (isOk)
            {
                if (vm.IsEditMode)
                {
                    _installerService.UpdateProcess(vm.CreatedProcess!);
                    _log.Info($"Updated process: {vm.CreatedProcess!.Name}");
                }
                else
                {
                    _installerService.AddUserProcess(vm.CreatedProcess!);
                    _log.Info($"User created process: {vm.CreatedProcess!.Name}");
                }

                RebuildExecutionQueue();
            }
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante la chiusura del pannello processo", ex);
        }
        finally
        {
            IsCreateProcessPanelOpen = false;
        }
    }

    private void OnCreateProcessDeleteRequested(object? sender, EventArgs e)
    {
        try
        {
            var vm = CreateProcessViewModel;
            if (!vm.IsEditMode || string.IsNullOrWhiteSpace(vm.EditingProcessId)) return;

            var confirmed = _dialogService.Confirm(
                "Elimina processo",
                $"Sei sicuro di voler eliminare il processo \"{vm.ProcessName}\"?");
            if (!confirmed) return;

            var deleted = _installerService.DeleteProcess(vm.EditingProcessId);
            if (!deleted)
            {
                vm.ValidationError = "Impossibile eliminare questo processo.";
                return;
            }

            foreach (var presetVm in Presets)
                presetVm.Refresh();

            RebuildExecutionQueue();
            IsCreateProcessPanelOpen = false;
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'eliminazione del processo", ex);
        }
    }

    private void OpenCreatePreset()
    {
        try
        {
            // Initialize the CreatePresetViewModel with all available processes
            var allProcesses = _installerService.GetAllAvailableProcesses();
            
            if (allProcesses == null)
            {
                _log.Error("Impossibile aprire il pannello: lista processi non disponibile.");
                return;
            }

            CreatePresetViewModel.Initialize(allProcesses);
            IsCreatePresetPanelOpen = true;
            _log.Info("Apertura pannello creazione preset.");
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'apertura del pannello creazione preset", ex);
        }
    }

    private void EditPreset(PresetViewModel? presetVm)
    {
        try
        {
            if (presetVm == null) return;

            // Initialize the CreatePresetViewModel for editing
            var allProcesses = _installerService.GetAllAvailableProcesses();
            
            if (allProcesses == null)
            {
                _log.Error("Impossibile modificare il preset: lista processi non disponibile.");
                return;
            }

            CreatePresetViewModel.InitializeForEdit(presetVm.Preset, allProcesses);
            IsCreatePresetPanelOpen = true;
            _log.Info($"Apertura pannello modifica preset: {presetVm.Name}");
        }
        catch (Exception ex)
        {
            _log.Error($"Errore durante l'apertura della modifica per il preset {presetVm?.Name}", ex);
        }
    }

    private void DeletePreset(PresetViewModel? presetVm)
    {
        try
        {
            if (presetVm is null) return;

            var confirmed = _dialogService.Confirm(
                "Elimina preset",
                $"Sei sicuro di voler eliminare il preset \"{presetVm.Name}\"?");
            if (!confirmed) return;

            var deleted = _installerService.DeletePreset(presetVm.Preset.Id);
            if (!deleted)
            {
                _log.Error($"Impossibile eliminare il preset: {presetVm.Name}");
                return;
            }

            _presetIconService.DeletePresetIcons(presetVm.Preset.Id);

            var wasSelected = presetVm.IsSelected;
            Presets.Remove(presetVm);
            ApplyPresetFilter();
            if (wasSelected) RebuildExecutionQueue();
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'eliminazione del preset", ex);
        }
    }

    private void DeleteProcess(ProcessStepViewModel? stepVm)
    {
        try
        {
            if (stepVm is null) return;

            var confirmed = _dialogService.Confirm(
                "Elimina processo",
                $"Sei sicuro di voler eliminare il processo \"{stepVm.Name}\"?\n\nQuesta operazione influirà su tutti i preset che lo utilizzano.");
            if (!confirmed) return;

            var deleted = _installerService.DeleteProcess(stepVm.Process.Id);
            if (!deleted)
            {
                _log.Error($"Impossibile eliminare il processo: {stepVm.Name}");
                return;
            }

            foreach (var presetVm in Presets)
                presetVm.Refresh();

            RebuildExecutionQueue();
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'eliminazione del processo", ex);
        }
    }

    private void OnCreatePresetCloseRequested(object? sender, EventArgs e)
    {
        IsCreatePresetPanelOpen = false;

        if (CreatePresetViewModel.CreatedPreset != null)
        {
            if (CreatePresetViewModel.IsEditMode)
            {
                // Editing existing preset
                _installerService.UpdatePreset(CreatePresetViewModel.CreatedPreset);
                
                // Find and update the PresetViewModel
                var existingVm = Presets.FirstOrDefault(p => p.Preset.Id == CreatePresetViewModel.CreatedPreset.Id);
                if (existingVm != null)
                {
                    bool wasSelected = existingVm.IsSelected;
                    existingVm.Refresh();
                    ApplyPresetFilter();
                    
                    if (wasSelected)
                    {
                        RebuildExecutionQueue();
                    }
                    
                    _log.Info($"Updated preset: {CreatePresetViewModel.CreatedPreset.Name}");
                }
            }
            else
            {
                // Creating new preset
                _installerService.AddUserPreset(CreatePresetViewModel.CreatedPreset);
                
                var presetVm = new PresetViewModel(CreatePresetViewModel.CreatedPreset);
                presetVm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PresetViewModel.IsSelected))
                        RebuildExecutionQueue();
                };
                Presets.Add(presetVm);
                ApplyPresetFilter();
                
                _log.Info($"Created new preset: {CreatePresetViewModel.CreatedPreset.Name}");
            }
        }
        else
        {
            _log.Info("Preset operation cancelled.");
        }
    }

    private void OnCreatePresetDeleteRequested(object? sender, EventArgs e)
    {
        try
        {
            var vm = CreatePresetViewModel;
            if (!vm.IsEditMode) return;

            var confirmed = _dialogService.Confirm(
                "Elimina preset",
                $"Sei sicuro di voler eliminare il preset \"{vm.Name}\"?");
            if (!confirmed) return;

            var deleted = _installerService.DeletePreset(vm.PresetId);
            if (!deleted)
            {
                vm.ValidationError = "Impossibile eliminare questo preset.";
                return;
            }

            _presetIconService.DeletePresetIcons(vm.PresetId);

            var presetVm = Presets.FirstOrDefault(p => p.Preset.Id == vm.PresetId);
            if (presetVm is not null)
            {
                var wasSelected = presetVm.IsSelected;
                Presets.Remove(presetVm);
                ApplyPresetFilter();
                if (wasSelected) RebuildExecutionQueue();
            }

            IsCreatePresetPanelOpen = false;
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'eliminazione del preset", ex);
        }
    }

    private void ClearPresetSearch()
    {
        PresetSearchText = string.Empty;
    }

    private void ClearProcessSearch()
    {
        ProcessSearchText = string.Empty;
    }

    public async Task<int> RunSingleProcessHeadlessAsync(string processId, System.Threading.CancellationToken ct = default)
    {
        var id = (processId ?? string.Empty).Trim();
        if (id.Length == 0) return 2;

        var all = _installerService.GetAllAvailableProcesses();
        var process = all.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (process is null)
        {
            _log.Error($"Headless run failed: process not found: {id}");
            return 2;
        }

        var step = new ProcessStepViewModel(process, order: 10, _dialogService, isInSelectedPreset: true, isRequired: false);
        step.SetIsEnabledSilently(true);

        var args = process.Arguments ?? string.Empty;
        if (process.RequiresLicense)
        {
            var licenses = await _licenseScraperService.FetchLicensesAsync();
            var key = _licenseScraperService.ExtractLicenseKey(licenses, process.Name, customerName: string.Empty);
            if (string.IsNullOrWhiteSpace(key))
            {
                _log.Warning($"License key not found for process: {process.Name}");
                return 3;
            }
            args = args.Replace("{LICENSE_KEY}", key);
        }

        try
        {
            var result = await RunDeploymentProcessAsync(step, process, args, ct);
            return result.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return 4;
        }
        catch (Exception ex)
        {
            _log.Error($"Headless run failed: {process.Name}", ex);
            return -1;
        }
    }

    private async Task RunQueueAsync()
    {
        var enabledSteps = ExecutionQueue.Where(s => s.IsEnabled).ToList();
        if (enabledSteps.Count == 0) return;

        ResetQueueVisuals();

        while (true)
        {
            var missing = enabledSteps.FirstOrDefault(s => s.Process.RequiresAuth && !IsAuthenticatedForProcess(s.Process));
            if (missing is null) break;

            var preferredPortalId = (missing.Process.PortalId ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(preferredPortalId) &&
                !string.Equals(_prefsService.Preferences.SelectedPortalId, preferredPortalId, StringComparison.OrdinalIgnoreCase))
            {
                _prefsService.Preferences.SelectedPortalId = preferredPortalId;
                _prefsService.Save();
            }

            OpenLogin();
            if (!IsAuthenticatedForProcess(missing.Process)) return;
        }

        IsRunning = true;
        OverallStatus = "Esecuzione in corso...";
        IsTerminalTabSelected = true;
        LogViewModel.ClearTerminal();
        _queueCts?.Cancel();
        _queueCts?.Dispose();
        _queueCts = new System.Threading.CancellationTokenSource();
        var ct = _queueCts.Token;

        try
        {
            var runtimeProcessMap = _installerService
                .GetAllAvailableProcesses()
                .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            IReadOnlyList<LicenseEntry>? licenses = null;

            foreach (var step in enabledSteps)
            {
                if (!step.IsEnabled)
                    continue;

                ct.ThrowIfCancellationRequested();

                _currentRunningStep = step;
                step.IsRunningStep = true;
                step.IsProgressIndeterminate = true;
                step.ProgressValue = 0;
                step.SetStatus("▶️", "In esecuzione...");
                _log.Info($"[{step.Order}] Starting: {step.Name}");

                var process = runtimeProcessMap.TryGetValue(step.Process.Id, out var p) ? p : step.Process;
                var args = process.Arguments ?? string.Empty;

                if (process.RequiresLicense)
                {
                    licenses ??= await _licenseScraperService.FetchLicensesAsync();
                    var key = _licenseScraperService.ExtractLicenseKey(licenses, process.Name, customerName: string.Empty);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        step.SetStatus("❌", "Licenza non trovata");
                        _log.Warning($"License key not found for process: {process.Name}");
                        OverallStatus = "Errore: licenza non trovata.";
                        return;
                    }

                    args = args.Replace("{LICENSE_KEY}", key);
                }

                var result = await RunDeploymentProcessAsync(step, process, args, ct);

                step.IsRunningStep = false;
                step.IsProgressIndeterminate = false;

                if (step.WasSkippedThisRun)
                {
                    step.ProgressValue = 0;
                    step.SetStatus("⏭️", "Skipped");
                    _log.Warning($"[{step.Order}] Skipped: {step.Name}");
                    continue;
                }

                if (result.ExitCode == 0 || result.ExitCode == 3010 || result.ExitCode == 1641)
                {
                    step.ProgressValue = 100;
                    step.SetStatus("✅", result.ExitCode == 0 ? "Completato" : "Completato (reboot)");
                    _log.Info($"[{step.Order}] Completed: {step.Name}");
                }
                else
                {
                    step.ProgressValue = 0;
                    static string FirstLine(string? s)
                    {
                        var v = (s ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
                        var idx = v.IndexOfAny(['\r', '\n']);
                        return idx >= 0 ? v[..idx].Trim() : v;
                    }

                    var detail = FirstLine(result.StdErr);
                    if (string.IsNullOrWhiteSpace(detail))
                        detail = FirstLine(result.StdOut);

                    var statusFallback = (step.StatusText ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(detail) &&
                        !string.IsNullOrWhiteSpace(statusFallback) &&
                        !statusFallback.StartsWith("In esecuzione", StringComparison.OrdinalIgnoreCase) &&
                        !statusFallback.StartsWith("Errore (exit", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(statusFallback, "Errore", StringComparison.OrdinalIgnoreCase))
                    {
                        detail = statusFallback;
                    }

                    var statusText = string.IsNullOrWhiteSpace(detail)
                        ? $"Errore (exit {result.ExitCode})"
                        : $"{detail} (exit {result.ExitCode})";

                    step.SetStatus("❌", statusText);
                    _log.Error($"[{step.Order}] Failed: {step.Name} (exit {result.ExitCode})" + (string.IsNullOrWhiteSpace(detail) ? string.Empty : $" — {detail}"));
                    OverallStatus = string.IsNullOrWhiteSpace(detail)
                        ? $"Errore: {step.Name} (exit {result.ExitCode})"
                        : $"Errore: {step.Name} — {detail} (exit {result.ExitCode})";
                    return;
                }
            }
            OverallStatus = $"Completato — {enabledSteps.Count} step eseguiti.";
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Execution queue cancelled.");
            OverallStatus = "Annullato.";
            if (_currentRunningStep is not null)
            {
                _currentRunningStep.IsRunningStep = false;
                _currentRunningStep.IsProgressIndeterminate = false;
                _currentRunningStep.SetStatus("⛔", "Annullato");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Queue execution failed", ex);
            OverallStatus = $"Errore: {ex.Message}";
        }
        finally
        {
            _currentRunningStep = null;
            IsRunning = false;
            _queueCts?.Dispose();
            _queueCts = null;
        }
    }

    private async Task<ProcessResult> RunDeploymentProcessAsync(ProcessStepViewModel step, DeploymentProcess process, string arguments, System.Threading.CancellationToken ct)
    {
        var installDir = NormalizeInstallDirectory(process.InstallDirectory);
        var rawArgs = (arguments ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(rawArgs) && rawArgs.Contains("{INSTALL_DIR}", StringComparison.OrdinalIgnoreCase))
            rawArgs = rawArgs.Replace("{INSTALL_DIR}", installDir ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var expandedArguments = Environment.ExpandEnvironmentVariables(rawArgs);

        if (process.SubProcesses is not null && process.SubProcesses.Count > 0)
        {
            if (process.Kind == ProcessKind.Installer &&
                process.InstallerSourceMode != InstallerSourceMode.StaticLocal &&
                !string.IsNullOrWhiteSpace(process.RelativePath))
            {
                var parentInstallerPath = Path.IsPathRooted(process.RelativePath)
                    ? process.RelativePath
                    : Path.Combine(AppContext.BaseDirectory, process.RelativePath);

                if (!File.Exists(parentInstallerPath))
                {
                    try
                    {
                        step.SetStatus("⏳", "Download installer...");
                        await _updateService.UpdateSingleInstallerAsync(process, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.Warning($"Installer download failed for '{process.Name}': {ex.GetType().Name}: {ex.Message}");
                    }

                    if (!File.Exists(parentInstallerPath))
                    {
                        var src = process.InstallerSourceMode == InstallerSourceMode.StaticWeb
                            ? (string.IsNullOrWhiteSpace(process.DownloadUrl) ? "URL non impostato" : process.DownloadUrl.Trim())
                            : (string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl) ? "Cartella web non impostata" : process.DownloadBaseFolderUrl.Trim());

                        var err = $"Installer non trovato prima dei sottoprocessi. Sorgente: {src}";
                        step.SetStatus("❌", "Download fallito");
                        return new ProcessResult(1, string.Empty, err);
                    }
                }
            }

            ProcessResult last = new(0, string.Empty, string.Empty);
            var total = process.SubProcesses.Count;
            for (var i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sp = process.SubProcesses[i];

                var spProcess = sp.Process is null ? null : CloneProcess(sp.Process);
                if (spProcess is not null)
                {
                    if (!string.IsNullOrWhiteSpace(sp.Name)) spProcess.Name = sp.Name.Trim();
                    if (!string.IsNullOrWhiteSpace(sp.RelativePath)) spProcess.RelativePath = sp.RelativePath.Trim();
                    if (!string.IsNullOrWhiteSpace(sp.Arguments)) spProcess.Arguments = sp.Arguments.Trim();
                    if (sp.RunAsAdmin.HasValue) spProcess.RunAsAdmin = sp.RunAsAdmin.Value;
                }
                else
                {
                    var relOrAbs = string.IsNullOrWhiteSpace(sp.RelativePath) ? process.RelativePath : sp.RelativePath;
                    if (string.IsNullOrWhiteSpace(relOrAbs))
                        throw new InvalidOperationException($"Sub-process path missing for process: {process.Name}");

                    var legacyKind = InferKindFromPath(relOrAbs);
                    spProcess = new DeploymentProcess
                    {
                        Id = string.Empty,
                        Name = string.IsNullOrWhiteSpace(sp.Name) ? $"Step {i + 1}" : sp.Name.Trim(),
                        Kind = legacyKind,
                        InstallerSourceMode = InstallerSourceMode.StaticLocal,
                        RelativePath = relOrAbs.Trim(),
                        Arguments = (string.IsNullOrWhiteSpace(sp.Arguments) ? expandedArguments : sp.Arguments).Trim(),
                        RunAsAdmin = sp.RunAsAdmin ?? process.RunAsAdmin,
                        RequiresInternet = false,
                        IsRequired = false,
                        EnabledByDefault = true,
                        IsUserCreated = true
                    };
                }

                var stepName = string.IsNullOrWhiteSpace(spProcess.Name) ? $"Step {i + 1}" : spProcess.Name.Trim();
                var subArgsRaw = string.IsNullOrWhiteSpace(sp.Arguments)
                    ? (string.IsNullOrWhiteSpace(spProcess.Arguments) ? expandedArguments : spProcess.Arguments)
                    : sp.Arguments;
                var subArgs = Environment.ExpandEnvironmentVariables(subArgsRaw ?? string.Empty);

                step.IsProgressIndeterminate = true;
                step.SetStatus("▶️", $"{process.Name} — {stepName}");

                last = await RunDeploymentProcessAsync(step, spProcess, subArgs, ct);
                if (last.ExitCode != 0 && last.ExitCode != 3010 && last.ExitCode != 1641)
                    return last;
            }

            return last;
        }

        switch (process.Kind)
        {
            case ProcessKind.Installer:
            {
                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Installer path missing for process: {process.Name}");

                var installerPath = string.IsNullOrWhiteSpace(process.RelativePath)
                    ? string.Empty
                    : (Path.IsPathRooted(process.RelativePath)
                        ? process.RelativePath
                        : Path.Combine(AppContext.BaseDirectory, process.RelativePath));

                var shouldAutoUpdateBeforeRun =
                    process.InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
                    process.DownloadUseLatestVersion;

                if (!string.IsNullOrWhiteSpace(installerPath) &&
                    (shouldAutoUpdateBeforeRun || !File.Exists(installerPath)) &&
                    process.InstallerSourceMode != InstallerSourceMode.StaticLocal)
                {
                    string? downloadError = null;
                    try
                    {
                        step.SetStatus("⏳", "Download installer...");
                        await _updateService.UpdateSingleInstallerAsync(process, ct);
                    }
                    catch (Exception ex)
                    {
                        downloadError = $"{ex.GetType().Name}: {ex.Message}";
                        _log.Warning($"Installer download failed for '{process.Name}': {ex.GetType().Name}: {ex.Message}");
                    }

                    if (!string.IsNullOrWhiteSpace(installerPath) && !File.Exists(installerPath))
                    {
                        var src = process.InstallerSourceMode == InstallerSourceMode.StaticWeb
                            ? (string.IsNullOrWhiteSpace(process.DownloadUrl) ? "URL non impostato" : process.DownloadUrl.Trim())
                            : (string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl) ? "Cartella web non impostata" : process.DownloadBaseFolderUrl.Trim());

                        var err = $"Installer non trovato dopo il download. Sorgente: {src}";
                        if (!string.IsNullOrWhiteSpace(downloadError))
                            err += $" — {downloadError}";

                        step.SetStatus("❌", "Download fallito");
                        return new ProcessResult(1, string.Empty, err);
                    }
                }

                if (!string.IsNullOrWhiteSpace(installerPath) && !File.Exists(installerPath))
                {
                    var err = $"Installer non trovato: {installerPath}";
                    step.SetStatus("❌", "Installer non trovato");
                    return new ProcessResult(1, string.Empty, err);
                }

                var ext = Path.GetExtension(installerPath).ToLowerInvariant();
                if (ext == ".msi")
                {
                    var msiArgs = string.IsNullOrWhiteSpace(installDir)
                        ? expandedArguments
                        : AppendMsiInstallDirIfMissing(expandedArguments, installDir);
                    return await RunMsiWithAppProgressAsync(step, installerPath, msiArgs, process.RunAsAdmin, ct);
                }

                if (ext == ".exe")
                {
                    var shouldExtract =
                        await LooksLikeDotNetZipSfxAsync(installerPath, ct) ||
                        IsLikelySelfExtractingArchiveExe(installerPath);
                    if (shouldExtract)
                    {
                        var retailLogDir = Path.Combine(AppContext.BaseDirectory, "Data", "retail-logs");
                        Directory.CreateDirectory(retailLogDir);

                        step.IsProgressIndeterminate = false;
                        step.ProgressValue = 0;
                        step.SetStatus("⏳", "Preparing...");

                        step.SetStatus("⏳", "Downloading 7-Zip...");
                        step.ProgressValue = 10;
                        var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync(ct);
                        step.SetStatus("⏳", "Installing 7-Zip...");
                        step.ProgressValue = 35;

                        var extractedDir = CreateProcessExtractionDir(process.Name);

                        _log.Info("Extracting with 7-Zip...");
                        step.SetStatus("⏳", "Extracting...");
                        step.ProgressValue = 55;

                        var extractArgs = $"x \"{installerPath}\" -o\"{extractedDir}\" -y";
                        var extractResult = await _processExecutionService.RunAsync(sevenZipExe, extractArgs, runAsAdmin: false, ct);
                        if (extractResult.ExitCode != 0)
                            throw new InvalidOperationException($"7-Zip extraction failed (exit {extractResult.ExitCode}).");

                        _log.Info($"Installing {process.Name}...");
                        step.SetStatus("▶️", "Installing...");
                        step.ProgressValue = 80;

                        if (process.RunAsAdmin && !IsRunningAsAdmin())
                            throw new InvalidOperationException("This installer is configured to run as Administrator. Restart KlevaDeploy as Administrator to avoid UAC prompts.");

                        var msis = FindExtractedMsis(extractedDir);
                        var mainMsi = ResolveInstallerFromExtractedDir(extractedDir);
                        if (!string.IsNullOrWhiteSpace(mainMsi) &&
                            !string.Equals(Path.GetExtension(mainMsi), ".msi", StringComparison.OrdinalIgnoreCase))
                        {
                            mainMsi = null;
                        }
                        if (msis.Count > 0)
                        {
                            var msiLogDir = Path.Combine(AppContext.BaseDirectory, "Data", "msi-logs");
                            Directory.CreateDirectory(msiLogDir);

                            step.SetStatus("⏳", "Installing prerequisites...");
                            step.ProgressValue = 70;

                            static int GetPrereqOrderScore(string msiPath)
                            {
                                var n = Path.GetFileName(msiPath) ?? string.Empty;
                                if (n.StartsWith("SetupRetail", StringComparison.OrdinalIgnoreCase)) return 10_000;
                                if (n.Contains("sqlncli", StringComparison.OrdinalIgnoreCase) ||
                                    n.Contains("nativeclient", StringComparison.OrdinalIgnoreCase) ||
                                    n.Contains("native client", StringComparison.OrdinalIgnoreCase))
                                {
                                    return 0;
                                }
                                if (n.Contains("SharedManagementObjects", StringComparison.OrdinalIgnoreCase)) return 10;
                                if (n.Contains("SQLSysClrTypes", StringComparison.OrdinalIgnoreCase)) return 20;
                                if (n.Contains("SqlCmd", StringComparison.OrdinalIgnoreCase) ||
                                    n.Contains("CmdLnUtils", StringComparison.OrdinalIgnoreCase))
                                {
                                    return 30;
                                }
                                return 100;
                            }

                            var prereqMsis = msis
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .OrderBy(GetPrereqOrderScore)
                                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            foreach (var msi in prereqMsis)
                            {
                                ct.ThrowIfCancellationRequested();
                                var fileName = Path.GetFileName(msi) ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(mainMsi) &&
                                    string.Equals(msi, mainMsi, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var logPath = Path.Combine(
                                    msiLogDir,
                                    $"KlevaDeploy_{Guid.NewGuid():N}_Prereq_{Path.GetFileNameWithoutExtension(msi)}.msi.log");

                                var msiArgs =
                                    $"/i \"{msi}\" /qn /norestart REBOOT=ReallySuppress ALLUSERS=2 /L*v \"{logPath}\"";

                                if (fileName.Contains("sqlncli", StringComparison.OrdinalIgnoreCase) ||
                                    fileName.Contains("nativeclient", StringComparison.OrdinalIgnoreCase) ||
                                    fileName.Contains("native client", StringComparison.OrdinalIgnoreCase))
                                {
                                    msiArgs = $"{msiArgs} IACCEPTSQLNCLILICENSETERMS=YES".Trim();
                                }

                                var msiResult = new ProcessResult(1618, string.Empty, string.Empty);
                                for (var attempt = 0; attempt < 25; attempt++)
                                {
                                    msiResult = await _processExecutionService.RunAsync("msiexec.exe", msiArgs, runAsAdmin: false, ct);
                                    if (msiResult.ExitCode != 1618)
                                        break;

                                    await Task.Delay(1000, ct);
                                }

                                if (msiResult.ExitCode != 0 && msiResult.ExitCode != 3010 && msiResult.ExitCode != 1641)
                                {
                                    static bool LogContainsAny(string path, params string[] needles)
                                    {
                                        try
                                        {
                                            if (!File.Exists(path)) return false;
                                            var text = File.ReadAllText(path);
                                            foreach (var n in needles)
                                            {
                                                if (string.IsNullOrWhiteSpace(n)) continue;
                                                if (text.Contains(n, StringComparison.OrdinalIgnoreCase))
                                                    return true;
                                            }
                                            return false;
                                        }
                                        catch
                                        {
                                            return false;
                                        }
                                    }

                                    var isSqlNcli = fileName.Contains("sqlncli", StringComparison.OrdinalIgnoreCase) ||
                                                    fileName.Contains("nativeclient", StringComparison.OrdinalIgnoreCase) ||
                                                    fileName.Contains("native client", StringComparison.OrdinalIgnoreCase);
                                    var isSqlCmd = fileName.Contains("SqlCmd", StringComparison.OrdinalIgnoreCase) ||
                                                   fileName.Contains("CmdLnUtils", StringComparison.OrdinalIgnoreCase);

                                    var unsupportedOs = LogContainsAny(logPath, "not supported on this operating system");
                                    var missingPrereq = LogContainsAny(logPath, "Setup is missing an installation prerequisite");

                                    if (isSqlNcli && unsupportedOs)
                                    {
                                        _log.Warning($"Skipping prerequisite (unsupported OS): {fileName}. Log: {logPath}");
                                        step.SetStatus("⚠️", $"Skipped prereq (unsupported OS): {fileName}");
                                        continue;
                                    }

                                    if (isSqlCmd && missingPrereq)
                                    {
                                        _log.Warning($"Skipping prerequisite (missing dependency): {fileName}. Log: {logPath}");
                                        step.SetStatus("⚠️", $"Skipped prereq (dependency missing): {fileName}");
                                        continue;
                                    }

                                    throw new InvalidOperationException($"MSI install failed: {Path.GetFileName(msi)} (exit {msiResult.ExitCode}). Log: {logPath}");
                                }
                            }
                        }

                        var setupExe = FindSetupExe(extractedDir);
                        if (string.IsNullOrWhiteSpace(setupExe))
                            throw new InvalidOperationException($"setup.exe not found after extraction: {installerPath}");

                        if (!string.IsNullOrWhiteSpace(mainMsi) && File.Exists(mainMsi))
                        {
                            var msiLogDir = Path.Combine(AppContext.BaseDirectory, "Data", "msi-logs");
                            Directory.CreateDirectory(msiLogDir);

                            var logPath = Path.Combine(
                                msiLogDir,
                                $"KlevaDeploy_{Guid.NewGuid():N}_Main_{Path.GetFileNameWithoutExtension(mainMsi)}.msi.log");

                            static string SetMsiProp(string tail, string key, string value)
                            {
                                if (string.IsNullOrWhiteSpace(tail)) return $"{key}={value}";

                                var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                var updated = false;
                                for (var i = 0; i < parts.Length; i++)
                                {
                                    if (!parts[i].StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) continue;
                                    parts[i] = $"{key}={value}";
                                    updated = true;
                                }

                                if (!updated)
                                    return $"{tail} {key}={value}";
                                return string.Join(' ', parts).Trim();
                            }

                            static string EnsureSecureCustomProperties(string tail)
                            {
                                if (string.IsNullOrWhiteSpace(tail)) return tail;

                                const string secureKey = "SECURECUSTOMPROPERTIES";
                                static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';
                                static bool IsMsiPropertyName(string name)
                                {
                                    if (string.IsNullOrWhiteSpace(name)) return false;
                                    if (!IsNameChar(name[0]) || char.IsDigit(name[0])) return false;
                                    for (var i = 1; i < name.Length; i++)
                                    {
                                        if (!IsNameChar(name[i])) return false;
                                    }
                                    return true;
                                }

                                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                var existingIdx = -1;
                                var existingValue = string.Empty;

                                for (var i = 0; i < parts.Length; i++)
                                {
                                    var p = parts[i];
                                    if (p.StartsWith($"{secureKey}=", StringComparison.OrdinalIgnoreCase))
                                    {
                                        existingIdx = i;
                                        var eq = p.IndexOf('=');
                                        existingValue = eq >= 0 ? p[(eq + 1)..].Trim().Trim('"') : string.Empty;
                                        break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(existingValue))
                                {
                                    var existingParts = existingValue
                                        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                    foreach (var p in existingParts)
                                    {
                                        if (!string.IsNullOrWhiteSpace(p))
                                            names.Add(p.Trim().ToUpperInvariant());
                                    }
                                }

                                foreach (var p in parts)
                                {
                                    if (p.StartsWith("/") || p.StartsWith("-")) continue;
                                    var eq = p.IndexOf('=');
                                    if (eq <= 0) continue;

                                    var name = p[..eq].Trim();
                                    if (!IsMsiPropertyName(name)) continue;
                                    if (string.Equals(name, secureKey, StringComparison.OrdinalIgnoreCase)) continue;

                                    names.Add(name.ToUpperInvariant());
                                }

                                if (names.Count == 0) return tail;

                                var mergedToken = $"{secureKey}={string.Join(';', names)}";
                                if (existingIdx >= 0)
                                {
                                    parts[existingIdx] = mergedToken;
                                    return string.Join(' ', parts).Trim();
                                }

                                return $"{tail} {mergedToken}".Trim();
                            }

                            static string? TryGetPropValue(string tail, string key)
                            {
                                if (string.IsNullOrWhiteSpace(tail)) return null;
                                var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                foreach (var p in parts)
                                {
                                    if (!p.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) continue;
                                    return p[(key.Length + 1)..];
                                }
                                return null;
                            }

                            static string EnsureAlias(string tail, string fromKey, string toKey)
                            {
                                if (string.IsNullOrWhiteSpace(tail)) return tail;

                                var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                if (parts.Any(p => p.StartsWith($"{toKey}=", StringComparison.Ordinal)))
                                    return tail;

                                var v = TryGetPropValue(tail, fromKey);
                                if (string.IsNullOrWhiteSpace(v)) return tail;
                                return $"{tail} {toKey}={v}".Trim();
                            }

                            var msiPropTail = expandedArguments.Trim();
                            var isRetailMain = string.Equals(Path.GetFileName(mainMsi), "SetupRetail.msi", StringComparison.OrdinalIgnoreCase);
                            if (isRetailMain)
                            {
                                var retailLogPath = Path.Combine(retailLogDir, $"RetailInstall_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.log");
                                msiPropTail = SetMsiProp(msiPropTail, "LOGFILE", retailLogPath);

                                msiPropTail = EnsureAlias(msiPropTail, "INSTALLAZIONEAUTOMATICA", "InstallazioneAutomatica");
                                msiPropTail = EnsureAlias(msiPropTail, "REINSTALLMODE", "ReinstallMode");
                                msiPropTail = EnsureAlias(msiPropTail, "USABULK", "UsaBulk");
                                msiPropTail = EnsureAlias(msiPropTail, "LIVEUPDATE", "Liveupdate");
                                msiPropTail = EnsureAlias(msiPropTail, "IPSERVERDATABASE", "IpServerDatabase");
                                msiPropTail = EnsureAlias(msiPropTail, "PORTASERVER", "PortaServer");
                                msiPropTail = EnsureAlias(msiPropTail, "NOMEDATABASE", "NomeDatabase");
                                msiPropTail = EnsureAlias(msiPropTail, "PASSWORDDATABASE", "PasswordDatabase");
                                msiPropTail = EnsureAlias(msiPropTail, "LOGFILE", "LogFile");
                                msiPropTail = EnsureAlias(msiPropTail, "InstallazioneAutomatica", "INSTALLAZIONEAUTOMATICA");
                                msiPropTail = EnsureAlias(msiPropTail, "ReinstallMode", "REINSTALLMODE");
                                msiPropTail = EnsureAlias(msiPropTail, "UsaBulk", "USABULK");
                                msiPropTail = EnsureAlias(msiPropTail, "Liveupdate", "LIVEUPDATE");
                                msiPropTail = EnsureAlias(msiPropTail, "IpServerDatabase", "IPSERVERDATABASE");
                                msiPropTail = EnsureAlias(msiPropTail, "PortaServer", "PORTASERVER");
                                msiPropTail = EnsureAlias(msiPropTail, "NomeDatabase", "NOMEDATABASE");
                                msiPropTail = EnsureAlias(msiPropTail, "PasswordDatabase", "PASSWORDDATABASE");
                                msiPropTail = EnsureAlias(msiPropTail, "LogFile", "LOGFILE");
                                msiPropTail = SetMsiProp(msiPropTail, "INSTALLAZIONEAUTOMATICA", "true");
                                msiPropTail = SetMsiProp(msiPropTail, "USABULK", "false");
                                msiPropTail = SetMsiProp(msiPropTail, "LIVEUPDATE", "false");
                                msiPropTail = SetMsiProp(msiPropTail, "REINSTALLMODE", "amus");
                                msiPropTail = EnsureSecureCustomProperties(msiPropTail);
                            }

                            var msiArgs =
                                $"/i \"{mainMsi}\" /qn /norestart REBOOT=ReallySuppress ALLUSERS=2 /L*v \"{logPath}\" {msiPropTail}".Trim();
                            var msiResult = await _processExecutionService.RunAsync("msiexec.exe", msiArgs, runAsAdmin: false, ct);
                            if (msiResult.ExitCode != 0 && msiResult.ExitCode != 3010 && msiResult.ExitCode != 1641)
                                throw new InvalidOperationException($"MSI install failed: {Path.GetFileName(mainMsi)} (exit {msiResult.ExitCode}). Log: {logPath}");

                            step.ProgressValue = 95;
                            return msiResult;
                        }

                        var argTail = expandedArguments.Trim();
                        var installArgs = string.IsNullOrWhiteSpace(argTail) ? "/quiet /norestart" : $"/quiet /norestart {argTail}";
                        var installResult = await _processExecutionService.RunAsync(setupExe, installArgs, runAsAdmin: false, ct);
                        step.ProgressValue = 95;
                        return installResult;
                    }
                }

                return await _processExecutionService.RunAsync(installerPath, expandedArguments, process.RunAsAdmin, ct);
            }
            case ProcessKind.PowerShellScript:
            {
                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunPowerShellAsync(process.ScriptContent, isInlineScript: true, process.RunAsAdmin, ct);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"PowerShell script path missing for process: {process.Name}");

                var scriptPath = Path.IsPathRooted(process.RelativePath)
                    ? process.RelativePath
                    : Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunPowerShellAsync(scriptPath, isInlineScript: false, process.RunAsAdmin, ct);
            }
            case ProcessKind.BatchScript:
            {
                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunBatchAsync(process.ScriptContent, isInlineScript: true, process.RunAsAdmin, ct);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Batch script path missing for process: {process.Name}");

                var scriptPath = Path.IsPathRooted(process.RelativePath)
                    ? process.RelativePath
                    : Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunBatchAsync(scriptPath, isInlineScript: false, process.RunAsAdmin, ct);
            }
            case ProcessKind.BashScript:
            {
                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunBashAsync(process.ScriptContent, isInlineScript: true, ct);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Bash script path missing for process: {process.Name}");

                var scriptPath = Path.IsPathRooted(process.RelativePath)
                    ? process.RelativePath
                    : Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunBashAsync(scriptPath, isInlineScript: false, ct);
            }
            case ProcessKind.RegistryFile:
            {
                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Registry file path missing for process: {process.Name}");

                var regPath = Path.IsPathRooted(process.RelativePath)
                    ? process.RelativePath
                    : Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                var args = $"import \"{regPath}\"";
                return await _processExecutionService.RunAsync("reg.exe", args, process.RunAsAdmin, ct);
            }
            case ProcessKind.ConfigAction:
            default:
            {
                _log.Warning($"ConfigAction not implemented: {process.Name}");
                return new ProcessResult(0, string.Empty, string.Empty);
            }
        }
    }

    private static string NormalizeInstallDirectory(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
        try
        {
            if (!Path.IsPathRooted(v)) return string.Empty;
            var full = Path.GetFullPath(v);
            if (string.IsNullOrWhiteSpace(full)) return string.Empty;
            return full.TrimEnd('\\', '/');
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string AppendMsiInstallDirIfMissing(string args, string installDir)
    {
        var a = (args ?? string.Empty).Trim();
        if (a.Contains("INSTALLDIR=", StringComparison.OrdinalIgnoreCase) ||
            a.Contains("TARGETDIR=", StringComparison.OrdinalIgnoreCase))
            return a;

        var safe = (installDir ?? string.Empty).Replace("\"", "\\\"");
        if (string.IsNullOrWhiteSpace(safe)) return a;
        return (a.Length == 0 ? string.Empty : a + " ") + $"INSTALLDIR=\"{safe}\" TARGETDIR=\"{safe}\"";
    }

    private static ProcessKind InferKindFromPath(string path)
    {
        var ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".ps1" => ProcessKind.PowerShellScript,
            ".bat" => ProcessKind.BatchScript,
            ".cmd" => ProcessKind.BatchScript,
            ".sh" => ProcessKind.BashScript,
            ".reg" => ProcessKind.RegistryFile,
            _ => ProcessKind.Installer
        };
    }

    private static DeploymentProcess CloneProcess(DeploymentProcess p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Kind = p.Kind,
        RelativePath = p.RelativePath,
        Arguments = p.Arguments,
        DownloadUrl = p.DownloadUrl,
        DownloadBaseFolderUrl = p.DownloadBaseFolderUrl,
        DownloadSelectedFileName = p.DownloadSelectedFileName,
        DownloadSelectedFileTemplate = p.DownloadSelectedFileTemplate,
        DownloadPickLatestFolderByName = p.DownloadPickLatestFolderByName,
        InstallerSourceMode = p.InstallerSourceMode,
        DownloadUseLatestVersion = p.DownloadUseLatestVersion,
        DownloadVersionFolderName = p.DownloadVersionFolderName,
        RequiresAuth = p.RequiresAuth,
        PortalId = p.PortalId,
        RequiresLicense = p.RequiresLicense,
        LicenseExcelColumn = p.LicenseExcelColumn,
        EnabledByDefault = p.EnabledByDefault,
        IsRequired = p.IsRequired,
        DependsOn = p.DependsOn?.ToList() ?? new(),
        RunAsAdmin = p.RunAsAdmin,
        RequiresInternet = p.RequiresInternet,
        ScriptContent = p.ScriptContent,
        InstallDirectory = p.InstallDirectory,
        IconKey = p.IconKey,
        Icon = p.Icon,
        CustomIconLightPath = p.CustomIconLightPath,
        CustomIconDarkPath = p.CustomIconDarkPath,
        IsUserCreated = p.IsUserCreated,
        SubProcesses = p.SubProcesses?.ToList() ?? new()
    };

    private static string CreateProcessExtractionDir(string processName)
    {
        var safe = new string((processName ?? "Process")
            .Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))
            .Select(ch => ch == ' ' ? '_' : ch)
            .ToArray());

        if (string.IsNullOrWhiteSpace(safe))
            safe = "Process";

        var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        if (string.IsNullOrWhiteSpace(storageDir))
            storageDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var root = Path.Combine(storageDir, "temp", "Extracted");
        Directory.CreateDirectory(root);
        var dir = Path.Combine(root, $"{safe}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string? FindSetupExe(string extractedDir)
    {
        try
        {
            return Directory.EnumerateFiles(extractedDir, "setup.exe", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> FindExtractedMsis(string extractedDir)
    {
        try
        {
            var all = Directory.EnumerateFiles(extractedDir, "*.msi", SearchOption.AllDirectories).ToList();
            if (all.Count == 0) return Array.Empty<string>();

            static int Score(string path)
            {
                var name = Path.GetFileName(path) ?? string.Empty;
                if (name.Contains("sqlsysclrtypes", StringComparison.OrdinalIgnoreCase)) return 10;
                if (name.Contains("sharedmanagementobjects", StringComparison.OrdinalIgnoreCase)) return 20;
                if (name.Contains("managementobject", StringComparison.OrdinalIgnoreCase)) return 20;
                if (name.Contains("sqlncli", StringComparison.OrdinalIgnoreCase)) return 30;
                if (name.Contains("native", StringComparison.OrdinalIgnoreCase) && name.Contains("client", StringComparison.OrdinalIgnoreCase)) return 30;
                return 100;
            }

            return all
                .OrderBy(Score)
                .ThenBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> LooksLikeDotNetZipSfxAsync(string exePath, System.Threading.CancellationToken ct)
    {
        try
        {
            if (!File.Exists(exePath)) return false;
            await using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = fs.Length;
            if (len <= 0) return false;

            var window = (int)Math.Min(4L * 1024 * 1024, len);
            var buffer = new byte[window];
            var read = await fs.ReadAsync(buffer.AsMemory(0, window), ct);
            if (read <= 0) return false;

            var text = Encoding.ASCII.GetString(buffer, 0, read);
            if (text.Contains("DotNetZip", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("Ionic.Zip", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("Self Extractor", StringComparison.OrdinalIgnoreCase) &&
                text.Contains("Zip", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasUnrarExeInToolsFolder()
    {
        try
        {
            var localTools = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KlevaDeploy",
                "Tools",
                "unrar.exe");
            if (File.Exists(localTools)) return true;
        }
        catch { }

        try
        {
            var baseTools = Path.Combine(AppContext.BaseDirectory, "Tools", "unrar.exe");
            if (File.Exists(baseTools)) return true;
        }
        catch { }

        return false;
    }

    private static bool IsLikelySelfExtractingArchiveExe(string exePath)
    {
        try
        {
            var info = new FileInfo(exePath);
            if (!info.Exists) return false;
            if (info.Length < 1024 * 1024) return false;

            var patterns = new[]
            {
                new byte[] { 0x52, 0x61, 0x72, 0x21 },                         // "Rar!"
                new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C },             // 7z
                new byte[] { 0x50, 0x4B, 0x03, 0x04 },                         // zip (PK..)
            };

            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var window = (int)Math.Min(2L * 1024 * 1024, fs.Length);

            var head = new byte[window];
            _ = fs.Read(head, 0, head.Length);
            if (ContainsAnyPattern(head, patterns)) return true;

            if (fs.Length > window)
            {
                fs.Seek(-window, SeekOrigin.End);
                var tail = new byte[window];
                _ = fs.Read(tail, 0, tail.Length);
                if (ContainsAnyPattern(tail, patterns)) return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ContainsAnyPattern(byte[] haystack, IReadOnlyList<byte[]> patterns)
    {
        foreach (var p in patterns)
        {
            if (IndexOf(haystack, p) >= 0) return true;
        }
        return false;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return 0;
        if (haystack.Length < needle.Length) return -1;

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }

    private async Task<ProcessResult> RunMsiWithAppProgressAsync(
        ProcessStepViewModel step,
        string msiPath,
        string msiArgs,
        bool runAsAdmin,
        System.Threading.CancellationToken ct)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            throw new InvalidOperationException("Cannot resolve current executable path for MSI worker mode.");

        var pipeName = $"KlevaDeploy_MSI_{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            transmissionMode: PipeTransmissionMode.Byte,
            options: PipeOptions.Asynchronous);

        var workerArgs = BuildMsiWorkerArgs(pipeName, msiPath, msiArgs);
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = workerArgs,
            CreateNoWindow = true,
            UseShellExecute = runAsAdmin,
        };

        if (runAsAdmin)
            psi.Verb = "runas";

        Process? proc = null;
        try
        {
            proc = Process.Start(psi);
            if (proc is null)
                return new ProcessResult(-1, string.Empty, "Failed to start MSI worker process.");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to start MSI worker process", ex);
            return new ProcessResult(-1, string.Empty, ex.Message);
        }

        using var __ = ct.Register(() =>
        {
            try
            {
                if (!proc.HasExited)
                    proc.Kill(entireProcessTree: true);
            }
            catch { }
        });

        step.SetStatus("▶️", "Installing... 0%");
        _log.Info($"MSI install started (Admin: {runAsAdmin}): {Path.GetFileName(msiPath)}");

        var exitCodeFromWorker = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var readCts = new System.Threading.CancellationTokenSource();

        var readTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync(readCts.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

                while (true)
                {
                    readCts.Token.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync();
                    if (line is null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    MsiWorkerMessage? msg = null;
                    try
                    {
                        msg = JsonSerializer.Deserialize<MsiWorkerMessage>(line);
                    }
                    catch
                    {
                        _log.AppendRaw("MSI", line.Trim());
                        continue;
                    }

                    if (msg is null) continue;

                    switch ((msg.Type ?? string.Empty).Trim().ToLowerInvariant())
                    {
                        case "start":
                            if (!string.IsNullOrWhiteSpace(msg.LogPath))
                                _log.Info($"MSI log: {msg.LogPath}");
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI", msg.Message);
                            break;
                        case "progress":
                            if (msg.Percent is int p)
                            {
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    step.IsProgressIndeterminate = false;
                                    step.ProgressValue = p;
                                    step.SetStatus("▶️", $"Installing... {p}%");
                                });
                            }
                            break;
                        case "actionstart":
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI", $"Action: {msg.Message}");
                            break;
                        case "warning":
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI-WARN", msg.Message);
                            break;
                        case "error":
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI-ERROR", msg.Message);
                            break;
                        case "debug":
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI-DEBUG", msg.Message);
                            break;
                        case "info":
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI", msg.Message);
                            break;
                        case "done":
                            if (msg.ExitCode is int c)
                                exitCodeFromWorker.TrySetResult(c);
                            if (!string.IsNullOrWhiteSpace(msg.Message))
                                _log.AppendRaw("MSI", msg.Message);
                            if (!string.IsNullOrWhiteSpace(msg.LogPath))
                                _log.Info($"MSI log: {msg.LogPath}");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!readCts.IsCancellationRequested)
                    _log.Error("MSI worker pipe reader failed", ex);
            }
        }, readCts.Token);

        var waitProcTask = proc.WaitForExitAsync(ct);

        try
        {
            await waitProcTask;
        }
        finally
        {
            readCts.Cancel();
            try { await readTask; } catch { }
        }

        if (!exitCodeFromWorker.Task.IsCompleted)
            exitCodeFromWorker.TrySetResult(proc.ExitCode);

        var exitCode = await exitCodeFromWorker.Task;
        return new ProcessResult(exitCode, string.Empty, string.Empty);
    }

    private static string BuildMsiWorkerArgs(string pipeName, string msiPath, string msiArgs)
    {
        var sb = new StringBuilder();
        sb.Append("--msi-worker ");
        sb.Append("--pipe ");
        sb.Append('"').Append(pipeName.Replace("\"", "\"\"")).Append("\" ");
        sb.Append("--msi ");
        sb.Append('"').Append(msiPath.Replace("\"", "\"\"")).Append("\" ");
        sb.Append("--msi-args ");
        sb.Append('"').Append((msiArgs ?? string.Empty).Replace("\"", "\"\"")).Append('"');
        return sb.ToString();
    }

    private sealed class MsiWorkerMessage
    {
        public string? Type { get; set; }
        public string? Message { get; set; }
        public int? Percent { get; set; }
        public int? ExitCode { get; set; }
        public string? LogPath { get; set; }
    }

    private static string? ResolveInstallerFromExtractedDir(string extractedDir)
    {
        if (string.IsNullOrWhiteSpace(extractedDir) || !Directory.Exists(extractedDir))
            return null;

        static bool IsCandidate(string path)
        {
            var name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        var msiFiles = Directory.EnumerateFiles(extractedDir, "*.msi", SearchOption.AllDirectories)
            .Where(IsCandidate)
            .OrderByDescending(p =>
            {
                try { return new FileInfo(p).Length; } catch { return 0L; }
            })
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (msiFiles.Count > 0) return msiFiles[0];

        var exeFiles = Directory.EnumerateFiles(extractedDir, "*.exe", SearchOption.AllDirectories)
            .Where(IsCandidate)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? pickExe(string contains)
        {
            return exeFiles.FirstOrDefault(p =>
                Path.GetFileName(p).Contains(contains, StringComparison.OrdinalIgnoreCase));
        }

        var preferredExe = pickExe("setup") ?? pickExe("install") ?? pickExe("installer");
        if (!string.IsNullOrWhiteSpace(preferredExe)) return preferredExe;

        if (exeFiles.Count > 0) return exeFiles[0];

        return null;
    }

    private bool CanRunQueue() => ExecutionQueue.Count > 0 && !IsRunning && !IsInitializing;

    private bool CanCancelQueue() => IsRunning;

    private void CancelQueue()
    {
        if (!IsRunning) return;
        _queueCts?.Cancel();
    }

    private bool CanCancelStep(ProcessStepViewModel? step) =>
        step is not null && step.IsEnabled && (!IsRunning || step.StatusIcon != "✅");

    private void CancelStep(ProcessStepViewModel? step)
    {
        if (step is null) return;

        if (IsRunning && ReferenceEquals(step, _currentRunningStep))
        {
            CancelQueue();
            return;
        }

        if (IsRunning && step.IsRunningStep)
        {
            CancelQueue();
            return;
        }

        step.SetIsEnabledSilently(false);
        step.IsRunningStep = false;
        step.IsProgressIndeterminate = false;
        step.ProgressValue = 0;
        step.SetStatus("⛔", "Annullato");
        _log.Info($"Cancelled step: {step.Name}");
    }

    private void ResetQueueVisuals()
    {
        foreach (var step in ExecutionQueue)
        {
            step.ResetProgress();
            step.SetStatus("⏳", "In attesa");
        }
    }

    private void OpenLogin()
    {
        var beforeCount = _authService.AuthenticatedPortalCount;
        var vm = _loginVmFactory();
        var win = new LoginWindow(vm);
        var owner = System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current?.MainWindow;
        if (owner is null)
            win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        else
        {
            win.Owner = owner;
            win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        }
        win.ShowDialog();
        SyncAuthProperties();

        if (vm.RequestedSettingsSection is not null)
        {
            OpenSettings(vm.RequestedSettingsSection.Value, vm.RequestedPortalId);
            return;
        }

        if (vm.LoginSucceeded && IsAuthenticated)
        {
            Uri? portalUri = null;
            var portalHomeUrl = vm.SelectedPortal?.HomeUrl;
            var selectedPortalId = vm.SelectedPortal?.Id;
            if (Uri.TryCreate(portalHomeUrl?.Trim(), UriKind.Absolute, out var parsedPortal))
                portalUri = parsedPortal;

            if (IsCreateProcessPanelOpen &&
                CreateProcessViewModel.SelectedProcessKind == ProcessKind.Installer &&
                CreateProcessViewModel.InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
                !string.IsNullOrWhiteSpace(CreateProcessViewModel.DownloadBaseFolderUrl) &&
                CreateProcessViewModel.RefreshRemoteInstallerFilesCommand.CanExecute(null))
            {
                CreateProcessViewModel.PrepareAutoUpdateCheck();
                CreateProcessViewModel.RefreshRemoteInstallerFilesCommand.Execute(null);
            }

            List<DeploymentProcess> autoUpdateProcesses;
            if (portalUri is null)
            {
                autoUpdateProcesses = new();
            }
            else
            {
                autoUpdateProcesses = _allProcesses
                    .Where(p => p.Kind == ProcessKind.Installer &&
                                p.InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
                                p.DownloadUseLatestVersion &&
                                p.RequiresAuth &&
                                MatchesPortal(p, portalUri, selectedPortalId))
                    .ToList();
            }

            if (autoUpdateProcesses.Count > 0)
            {
                _ = Task.Run(() => _updateService.CheckAndUpdateInstallersAsync(autoUpdateProcesses));
            }

            if (_authService.AuthenticatedPortalCount != beforeCount)
                RefreshLoginBadges();
        }

        static bool MatchesPortal(DeploymentProcess p, Uri portalUri, string? selectedPortalId)
        {
            if (!string.IsNullOrWhiteSpace(p.PortalId) && !string.IsNullOrWhiteSpace(selectedPortalId))
                return string.Equals(p.PortalId, selectedPortalId, StringComparison.OrdinalIgnoreCase);

            var url = p.InstallerSourceMode == InstallerSourceMode.DynamicWeb ? p.DownloadBaseFolderUrl : p.DownloadUrl;
            if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var processUri))
                return false;
            return string.Equals(processUri.Host, portalUri.Host, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void Logout()
    {
        _authService.Logout();
        IsAuthenticated = false;
    }

    private void OpenSettings()
    {
        OpenSettings(SettingsSection.InfoEAggiornamenti, null);
    }

    private void OpenSettings(SettingsSection initialSection, string? initialPortalId)
    {
        var vm = new SettingsViewModel(_appUpdateService, _prefsService, _log, _dialogService, _presetIconService);
        vm.SelectedSection = initialSection;
        if (!string.IsNullOrWhiteSpace(initialPortalId))
            vm.SelectedPortal = vm.Portals.FirstOrDefault(p => string.Equals(p.Id, initialPortalId, StringComparison.OrdinalIgnoreCase));
        vm.LoadInstallers(_allProcesses);
        var win = new SettingsWindow(vm);
        var owner = System.Windows.Application.Current?.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w.IsActive)
                    ?? System.Windows.Application.Current?.MainWindow;
        if (owner is null)
            win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        else
        {
            win.Owner = owner;
            win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        }
        win.ShowDialog();
    }

    private async Task CheckAppUpdateAsync()
    {
        try
        {
            var info = await _appUpdateService.CheckForUpdateAsync();
            _pendingAppUpdate = info;

            if (info is null)
            {
                IsAppUpdateAvailable = false;
                AvailableAppVersion = string.Empty;
            }
            else
            {
                IsAppUpdateAvailable = true;
                AvailableAppVersion = info.Version;
            }
        }
        catch (Exception ex)
        {
            _log.Error("App update check failed", ex);
            IsAppUpdateAvailable = false;
            AvailableAppVersion = string.Empty;
        }
    }

    private bool CanDownloadAndRestartForUpdate() =>
        IsAppUpdateAvailable && !IsDownloadingAppUpdate && _pendingAppUpdate is not null;

    private async Task DownloadAndRestartForUpdateAsync()
    {
        if (_pendingAppUpdate is null) return;
        if (string.IsNullOrWhiteSpace(Environment.ProcessPath)) return;

        IsDownloadingAppUpdate = true;
        try
        {
            var path = await _appUpdateService.DownloadUpdateAsync(_pendingAppUpdate);
            if (string.IsNullOrWhiteSpace(path)) return;

            _downloadedAppUpdatePath = path;
            var pid = Environment.ProcessId;
            var target = Environment.ProcessPath!;

            Process.Start(new ProcessStartInfo(path, $"--apply-update --pid {pid} --target \"{target}\"")
            {
                UseShellExecute = true
            });

            var app = System.Windows.Application.Current;
            if (app is not null) app.Shutdown();
            else Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _log.Error("App update download failed", ex);
        }
        finally
        {
            IsDownloadingAppUpdate = false;
        }
    }

    private void ToggleTheme()
    {
        _themeService.ToggleTheme();
        SyncThemeProperties();
    }

    private void SyncThemeProperties()
    {
        IsDarkTheme = _themeService.CurrentTheme == AppTheme.Dark;
        ThemeToggleTooltip = _themeService.CurrentTheme == AppTheme.Dark
            ? "Passa al tema chiaro"
            : "Passa al tema scuro";
    }
}
