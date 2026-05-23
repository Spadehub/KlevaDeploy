using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    public LogViewModel LogViewModel { get; }
    public CreatePresetViewModel CreatePresetViewModel { get; }
    public CreateProcessViewModel CreateProcessViewModel { get; }

    public IAsyncRelayCommand InitializeCommand { get; }
    public IRelayCommand CreateProcessCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> EditProcessCommand { get; }
    public IRelayCommand OpenCreatePresetCommand { get; }
    public IRelayCommand<PresetViewModel?> EditPresetCommand { get; }
    public IRelayCommand ClearPresetSearchCommand { get; }
    public IRelayCommand ClearProcessSearchCommand { get; }
    public IAsyncRelayCommand RunQueueCommand { get; }
    public IRelayCommand OpenLoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IRelayCommand ToggleThemeCommand { get; }
    public IRelayCommand SetPresetListViewCommand { get; }
    public IRelayCommand SetPresetGridViewCommand { get; }

    public MainViewModel(
        IInstallerService installerService,
        IUpdateService updateService,
        IAuthService authService,
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
        _log = log;
        _themeService = themeService;
        _dialogService = dialogService;
        _presetIconService = presetIconService;
        _prefsService = prefsService;
        _loginVmFactory = loginVmFactory;
        LogViewModel = logViewModel;

        CreatePresetViewModel = new CreatePresetViewModel(_presetIconService);
        CreatePresetViewModel.CloseRequested += OnCreatePresetCloseRequested;

        CreateProcessViewModel = new CreateProcessViewModel();
        CreateProcessViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CreateProcessViewModel.DialogResult)) return;
            if (CreateProcessViewModel.DialogResult is null) return;
            OnCreateProcessCloseRequested();
        };

        SyncThemeProperties();

        IsPresetGridView = _prefsService.Preferences.PresetsViewMode == PresetsViewMode.Grid;

        InitializeCommand = new AsyncRelayCommand(InitializeAsync);
        CreateProcessCommand = new RelayCommand(CreateProcess);
        EditProcessCommand = new RelayCommand<ProcessStepViewModel?>(EditProcess);
        OpenCreatePresetCommand = new RelayCommand(OpenCreatePreset);
        EditPresetCommand = new RelayCommand<PresetViewModel?>(EditPreset);
        ClearPresetSearchCommand = new RelayCommand(ClearPresetSearch);
        ClearProcessSearchCommand = new RelayCommand(ClearProcessSearch);
        RunQueueCommand = new AsyncRelayCommand(RunQueueAsync, CanRunQueue);
        OpenLoginCommand = new RelayCommand(OpenLogin);
        LogoutCommand = new RelayCommand(Logout);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        SetPresetListViewCommand = new RelayCommand(() => IsPresetGridView = false);
        SetPresetGridViewCommand = new RelayCommand(() => IsPresetGridView = true);

        ExecutionQueue.CollectionChanged += (_, _) => RunQueueCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        await LoadDataAsync();
        // ISSUE 3 FIX: Rebuild execution queue at startup to show all processes
        RebuildExecutionQueue();
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
        }
    }

    private bool CanRunQueue() => ExecutionQueue.Count > 0 && !IsRunning && !IsInitializing;

    private void OpenLogin()
    {
        if (_authService.IsAuthenticated) { IsAuthenticated = true; return; }
        var vm = _loginVmFactory();
        var win = new LoginWindow(vm);
        win.ShowDialog();
        IsAuthenticated = _authService.IsAuthenticated;
    }

    private void Logout()
    {
        _authService.Logout();
        IsAuthenticated = false;
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
