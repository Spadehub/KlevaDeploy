using System.Collections.ObjectModel;
using System.Linq;
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
    private readonly List<DeploymentProcess> _userCreatedProcesses = new();
    private IReadOnlyList<DeploymentPreset> _allPresets = Array.Empty<DeploymentPreset>();
    private readonly Dictionary<string, bool> _userManualDeselections = new();

    public ObservableCollection<PresetViewModel> Presets { get; } = new();
    public ObservableCollection<PresetViewModel> FilteredPresets { get; } = new();
    public ObservableCollection<ProcessStepViewModel> ExecutionQueue { get; } = new();
    public ObservableCollection<ProcessStepViewModel> FilteredExecutionQueue { get; } = new();

    [ObservableProperty] private bool _isInitializing;
    [ObservableProperty] private bool _isAuthenticated;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _overallStatus = "Pronto";
    [ObservableProperty] private int _selectedPresetCount;
    [ObservableProperty] private string _themeToggleTooltip = "Passa al tema chiaro";
    [ObservableProperty] private bool _isDemoMode = true;
    [ObservableProperty] private string _presetSearchText = string.Empty;
    [ObservableProperty] private string _processSearchText = string.Empty;
    [ObservableProperty] private bool _isCreatePresetPanelOpen;

    public LogViewModel LogViewModel { get; }
    public CreatePresetViewModel CreatePresetViewModel { get; }

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

        CreatePresetViewModel = new CreatePresetViewModel();
        CreatePresetViewModel.CloseRequested += OnCreatePresetCloseRequested;

        SyncThemeProperties();
    }

    [RelayCommand]
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

    partial void OnIsDemoModeChanged(bool value)
    {
        _log.Info($"Demo mode changed to: {value}");
        _ = LoadDataAsync();
    }

    partial void OnPresetSearchTextChanged(string value)
    {
        ApplyPresetFilter();
    }

    partial void OnProcessSearchTextChanged(string value)
    {
        ApplyProcessFilter();
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

    [RelayCommand]
    private void CreateProcess()
    {
        var vm = new CreateProcessViewModel();
        var dialog = new CreateProcessDialog(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && vm.CreatedProcess != null)
        {
            _installerService.AddUserProcess(vm.CreatedProcess);
            _log.Info($"User created process: {vm.CreatedProcess.Name}");
            
            // Rebuild queue to include new process
            RebuildExecutionQueue();
        }
    }

    [RelayCommand]
    private void EditProcess(ProcessStepViewModel stepVm)
    {
        var vm = new CreateProcessViewModel();
        vm.InitializeForEdit(stepVm.Process);
        
        var dialog = new CreateProcessDialog(vm)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && vm.CreatedProcess != null)
        {
            _installerService.UpdateProcess(vm.CreatedProcess);
            _log.Info($"Updated process: {vm.CreatedProcess.Name}");
            
            // Rebuild queue to reflect changes
            RebuildExecutionQueue();
        }
    }

    [RelayCommand]
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

    [RelayCommand]
    private void EditPreset(PresetViewModel presetVm)
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

    [RelayCommand]
    private void ClearPresetSearch()
    {
        PresetSearchText = string.Empty;
    }

    [RelayCommand]
    private void ClearProcessSearch()
    {
        ProcessSearchText = string.Empty;
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
