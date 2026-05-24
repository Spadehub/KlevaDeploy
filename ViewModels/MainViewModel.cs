using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

    public ObservableCollection<PresetViewModel> Presets { get; } = new();
    public ObservableCollection<PresetViewModel> FilteredPresets { get; } = new();
    public ObservableCollection<ProcessStepViewModel> ExecutionQueue { get; } = new();
    public ObservableCollection<ProcessStepViewModel> FilteredExecutionQueue { get; } = new();

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
        set => SetProperty(ref _isAuthenticated, value);
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            RunQueueCommand.NotifyCanExecuteChanged();
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
    public IAsyncRelayCommand<ProcessStepViewModel?> UpdateInstallerCommand { get; }
    public IAsyncRelayCommand<ProcessStepViewModel?> RedownloadInstallerCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> RevealInstallerInExplorerCommand { get; }
    public IAsyncRelayCommand CheckAppUpdateCommand { get; }
    public IAsyncRelayCommand DownloadAndRestartForUpdateCommand { get; }
    public IRelayCommand OpenLoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
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

        CreateProcessViewModel = new CreateProcessViewModel(_authService, _downloadDirectoryListingService, _log);
        CreateProcessViewModel.DeleteRequested += OnCreateProcessDeleteRequested;
        CreateProcessViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CreateProcessViewModel.DialogResult)) return;
            if (CreateProcessViewModel.DialogResult is null) return;
            OnCreateProcessCloseRequested();
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
        UpdateInstallerCommand = new AsyncRelayCommand<ProcessStepViewModel?>(UpdateInstallerAsync, CanUpdateInstaller);
        RedownloadInstallerCommand = new AsyncRelayCommand<ProcessStepViewModel?>(RedownloadInstallerAsync, CanRedownloadInstaller);
        RevealInstallerInExplorerCommand = new RelayCommand<ProcessStepViewModel?>(RevealInstallerInExplorer, CanRevealInstallerInExplorer);
        CheckAppUpdateCommand = new AsyncRelayCommand(CheckAppUpdateAsync);
        DownloadAndRestartForUpdateCommand = new AsyncRelayCommand(DownloadAndRestartForUpdateAsync, CanDownloadAndRestartForUpdate);
        OpenLoginCommand = new RelayCommand(OpenLogin);
        LogoutCommand = new RelayCommand(Logout);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        SetPresetListViewCommand = new RelayCommand(() => IsPresetGridView = false);
        SetPresetGridViewCommand = new RelayCommand(() => IsPresetGridView = true);

        ExecutionQueue.CollectionChanged += (_, _) => RunQueueCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        IsAuthenticated = await _authService.TryRestoreSessionAsync();
        await LoadDataAsync();
        // ISSUE 3 FIX: Rebuild execution queue at startup to show all processes
        RebuildExecutionQueue();
        _ = CheckAppUpdateAsync();
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
        var searchLower = ProcessSearchText?.ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(searchLower)
            ? ExecutionQueue
            : ExecutionQueue.Where(p => p.Name.ToLowerInvariant().Contains(searchLower));

        foreach (var process in filtered)
        {
            FilteredExecutionQueue.Add(process);
        }
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
        IsTerminalTabSelected = true;
        LogViewModel.ClearTerminal();

        try
        {
            IReadOnlyList<LicenseEntry>? licenses = null;

            foreach (var step in enabledSteps)
            {
                step.SetStatus("▶️", "In esecuzione...");
                _log.Info($"[{step.Order}] Starting: {step.Name}");

                var process = step.Process;
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

                var result = await RunDeploymentProcessAsync(process, args);

                if (result.ExitCode == 0)
                {
                    step.SetStatus("✅", "Completato");
                    _log.Info($"[{step.Order}] Completed: {step.Name}");
                }
                else
                {
                    step.SetStatus("❌", $"Errore (exit {result.ExitCode})");
                    _log.Error($"[{step.Order}] Failed: {step.Name} (exit {result.ExitCode})");
                    OverallStatus = $"Errore: {step.Name} (exit {result.ExitCode})";
                    return;
                }
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
        }
    }

    private async Task<ProcessResult> RunDeploymentProcessAsync(DeploymentProcess process, string arguments)
    {
        switch (process.Kind)
        {
            case ProcessKind.Installer:
            {
                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Installer path missing for process: {process.Name}");

                var installerPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunAsync(installerPath, arguments, process.RunAsAdmin);
            }
            case ProcessKind.PowerShellScript:
            {
                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunPowerShellAsync(process.ScriptContent, isInlineScript: true, process.RunAsAdmin);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"PowerShell script path missing for process: {process.Name}");

                var scriptPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunPowerShellAsync(scriptPath, isInlineScript: false, process.RunAsAdmin);
            }
            case ProcessKind.BatchScript:
            {
                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunBatchAsync(process.ScriptContent, isInlineScript: true, process.RunAsAdmin);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Batch script path missing for process: {process.Name}");

                var scriptPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunBatchAsync(scriptPath, isInlineScript: false, process.RunAsAdmin);
            }
            case ProcessKind.BashScript:
            {
                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunBashAsync(process.ScriptContent, isInlineScript: true);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Bash script path missing for process: {process.Name}");

                var scriptPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                return await _processExecutionService.RunBashAsync(scriptPath, isInlineScript: false);
            }
            case ProcessKind.RegistryFile:
            {
                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Registry file path missing for process: {process.Name}");

                var regPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
                var args = $"import \"{regPath}\"";
                return await _processExecutionService.RunAsync("reg.exe", args, process.RunAsAdmin);
            }
            case ProcessKind.ConfigAction:
            default:
            {
                _log.Warning($"ConfigAction not implemented: {process.Name}");
                return new ProcessResult(0, string.Empty, string.Empty);
            }
        }
    }

    private bool CanRunQueue() => ExecutionQueue.Count > 0 && !IsRunning && !IsInitializing;

    private void OpenLogin()
    {
        if (_authService.IsAuthenticated) { IsAuthenticated = true; return; }
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
        IsAuthenticated = _authService.IsAuthenticated;
    }

    private void Logout()
    {
        _authService.Logout();
        IsAuthenticated = false;
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
