using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.Utilities;
using KlevaDeploy.Views;

namespace KlevaDeploy.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private enum ExeExtractionMode
    {
        None,
        MainMsiOnly,
        AllMsis
    }

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
    private readonly Dictionary<string, ArgumentPromptChoice> _argumentPromptChoiceThisRun = new(StringComparer.OrdinalIgnoreCase);

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
    public IAsyncRelayCommand ImportPackageCommand { get; }
    public IAsyncRelayCommand<PresetViewModel?> ExportPackageCommand { get; }
    public IAsyncRelayCommand ImportPackageIntoEditorCommand { get; }
    public IAsyncRelayCommand ExportPackageFromEditorCommand { get; }
    public IRelayCommand ImportProcessLibraryCommand { get; }
    public IRelayCommand ClearPresetSearchCommand { get; }
    public IRelayCommand ClearProcessSearchCommand { get; }
    public IAsyncRelayCommand RunQueueCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> MoveQueueStepUpCommand { get; }
    public IRelayCommand<ProcessStepViewModel?> MoveQueueStepDownCommand { get; }
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
        ImportPackageCommand = new AsyncRelayCommand(ImportPackageAsync);
        ExportPackageCommand = new AsyncRelayCommand<PresetViewModel?>(ExportPackageAsync);
        ImportPackageIntoEditorCommand = new AsyncRelayCommand(ImportPackageIntoEditorAsync);
        ExportPackageFromEditorCommand = new AsyncRelayCommand(ExportPackageFromEditorAsync);
        ImportProcessLibraryCommand = new RelayCommand(ImportProcessLibrary);
        ClearPresetSearchCommand = new RelayCommand(ClearPresetSearch);
        ClearProcessSearchCommand = new RelayCommand(ClearProcessSearch);
        RunQueueCommand = new AsyncRelayCommand(RunQueueAsync, CanRunQueue);
        MoveQueueStepUpCommand = new RelayCommand<ProcessStepViewModel?>(MoveQueueStepUp, CanMoveQueueStepUp);
        MoveQueueStepDownCommand = new RelayCommand<ProcessStepViewModel?>(MoveQueueStepDown, CanMoveQueueStepDown);
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

        ExecutionQueue.CollectionChanged += (_, _) =>
        {
            RunQueueCommand.NotifyCanExecuteChanged();
            MoveQueueStepUpCommand.NotifyCanExecuteChanged();
            MoveQueueStepDownCommand.NotifyCanExecuteChanged();
        };
    }

    private async Task ImportPackageIntoEditorAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importa pacchetto (nel pannello)",
            Filter = "Pacchetto KlevaDeploy (*.kdp.package.json;*.json)|*.kdp.package.json;*.json|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = await File.ReadAllTextAsync(dlg.FileName);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            JsonElement presetElem;
            if (doc.RootElement.TryGetProperty("package", out var pkgElem))
                presetElem = pkgElem;
            else if (doc.RootElement.TryGetProperty("preset", out var legacyPresetElem))
                presetElem = legacyPresetElem;
            else
                throw new InvalidOperationException("File pacchetto non valido. Manca la proprietà 'package'.");

            var preset = JsonSerializer.Deserialize<DeploymentPreset>(presetElem.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (preset is null)
                throw new InvalidOperationException("File pacchetto non valido.");

            var bundledProcesses = new List<DeploymentProcess>();
            if (doc.RootElement.TryGetProperty("processes", out var processesElem) &&
                processesElem.ValueKind == JsonValueKind.Array)
            {
                bundledProcesses = JsonSerializer.Deserialize<List<DeploymentProcess>>(processesElem.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                }) ?? new List<DeploymentProcess>();
            }

            if (bundledProcesses.Count > 0)
            {
                var importProcesses = _dialogService.Confirm(
                    "Importa processi associati",
                    $"Il file contiene {bundledProcesses.Count} processi associati.\n\nVuoi importarli insieme al pacchetto?");

                if (importProcesses)
                {
                    foreach (var process in bundledProcesses.Where(p => !string.IsNullOrWhiteSpace(p.Id)))
                    {
                        var existingProcess = _installerService.GetAllAvailableProcesses()
                            .FirstOrDefault(p => string.Equals(p.Id, process.Id, StringComparison.OrdinalIgnoreCase));

                        if (existingProcess is null)
                            _installerService.AddUserProcess(process);
                        else
                            _installerService.UpdateProcess(process);
                    }
                }
            }

            preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? BuildPackageId(preset.Name) : preset.Id.Trim();
            preset.Steps ??= new List<PresetProcessStep>();

            var allProcesses = _installerService.GetAllAvailableProcesses();
            CreatePresetViewModel.InitializeForEdit(preset, allProcesses);
            IsCreatePresetPanelOpen = true;

            RefreshPresetProcessAvailability();
            OverallStatus = $"Pacchetto caricato nel pannello: {preset.Name}";
        }
        catch (Exception ex)
        {
            _log.Error("Import pacchetto nel pannello fallito", ex);
            OverallStatus = $"Errore: import pacchetto fallito — {ex.Message}";
        }
    }

    private async Task ExportPackageFromEditorAsync()
    {
        try
        {
            if (!CreatePresetViewModel.TryBuildPreset(out var preset, out var error) || preset is null)
            {
                OverallStatus = $"Errore: export pacchetto fallito — {error ?? "dati non validi"}";
                return;
            }

            var safeName = string.Join("_", (preset.Name ?? "Pacchetto")
                    .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Pacchetto";

            var dlg = new SaveFileDialog
            {
                Title = "Esporta pacchetto (dal pannello)",
                Filter = "Pacchetto KlevaDeploy (*.kdp.package.json)|*.kdp.package.json|JSON (*.json)|*.json",
                FileName = $"{safeName}.kdp.package.json",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dlg.ShowDialog() != true) return;

            var includeProcesses = _dialogService.Confirm(
                "Esporta processi associati",
                "Vuoi includere nel file anche i processi associati al pacchetto?\n\nScegliendo \"No\" verrà esportato solo il pacchetto.");

            List<DeploymentProcess>? processes = null;
            if (includeProcesses)
            {
                var processIds = preset.Steps
                    .Select(step => step.ProcessId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                processes = _installerService.GetAllAvailableProcesses()
                    .Where(process => processIds.Contains(process.Id))
                    .Select(MaterializeExternalScriptsForExport)
                    .ToList();
            }

            var dto = new PackageBundleDto
            {
                SchemaVersion = 1,
                Package = preset,
                Processes = processes is { Count: > 0 } ? processes : null
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dlg.FileName, json);
            OverallStatus = $"Pacchetto esportato: {preset.Name}";
        }
        catch (Exception ex)
        {
            _log.Error("Export pacchetto dal pannello fallito", ex);
            OverallStatus = $"Errore: export pacchetto fallito — {ex.Message}";
        }
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
            _allProcesses = await _installerService.LoadProcessesAsync();
            _allPresets = await _installerService.LoadPresetsAsync();
            
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

            RefreshPresetProcessAvailability(_allProcesses);

            ApplyPresetFilter();
            _log.Info($"Loaded {_allPresets.Count} packages and {_allProcesses.Count} processes.");

            _ = Task.Run(() => _updateService.CheckAndUpdateInstallersAsync(_allProcesses));
        }
        finally { IsInitializing = false; }
    }

    private sealed class PackageBundleDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("package")]
        public DeploymentPreset? Package { get; set; }

        [JsonPropertyName("processes")]
        public List<DeploymentProcess>? Processes { get; set; }
    }

    private static string BuildPackageId(string name)
    {
        var raw = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (raw.Length == 0) return Guid.NewGuid().ToString("N");
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
            if (ch is ' ' or '_' or '-') { sb.Append('-'); continue; }
        }
        var id = sb.ToString().Trim('-');
        while (id.Contains("--", StringComparison.Ordinal)) id = id.Replace("--", "-", StringComparison.Ordinal);
        if (id.Length == 0) return Guid.NewGuid().ToString("N");
        return id;
    }

    private void RefreshPresetProcessAvailability(IReadOnlyList<DeploymentProcess>? availableProcesses = null)
    {
        var processIds = (availableProcesses ?? _installerService.GetAllAvailableProcesses())
            .Select(p => p.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var presetVm in Presets)
            presetVm.UpdateProcessAvailability(processIds);
    }

    private async Task ImportPackageAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importa pacchetto",
            Filter = "Pacchetto KlevaDeploy (*.kdp.package.json;*.json)|*.kdp.package.json;*.json|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = await File.ReadAllTextAsync(dlg.FileName);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            JsonElement presetElem;
            if (doc.RootElement.TryGetProperty("package", out var pkgElem))
                presetElem = pkgElem;
            else if (doc.RootElement.TryGetProperty("preset", out var legacyPresetElem))
                presetElem = legacyPresetElem;
            else
                throw new InvalidOperationException("File pacchetto non valido. Manca la proprietà 'package'.");

            var preset = JsonSerializer.Deserialize<DeploymentPreset>(presetElem.GetRawText(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (preset is null)
                throw new InvalidOperationException("File pacchetto non valido.");

            var bundledProcesses = new List<DeploymentProcess>();
            if (doc.RootElement.TryGetProperty("processes", out var processesElem) &&
                processesElem.ValueKind == JsonValueKind.Array)
            {
                bundledProcesses = JsonSerializer.Deserialize<List<DeploymentProcess>>(processesElem.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                }) ?? new List<DeploymentProcess>();
            }

            if (bundledProcesses.Count > 0)
            {
                var importProcesses = _dialogService.Confirm(
                    "Importa processi associati",
                    $"Il file contiene {bundledProcesses.Count} processi associati.\n\nVuoi importarli insieme al pacchetto?");

                if (importProcesses)
                {
                    foreach (var process in bundledProcesses.Where(p => !string.IsNullOrWhiteSpace(p.Id)))
                    {
                        var existingProcess = _installerService.GetAllAvailableProcesses()
                            .FirstOrDefault(p => string.Equals(p.Id, process.Id, StringComparison.OrdinalIgnoreCase));

                        if (existingProcess is null)
                            _installerService.AddUserProcess(process);
                        else
                            _installerService.UpdateProcess(process);
                    }
                }
            }

            preset.Id = string.IsNullOrWhiteSpace(preset.Id) ? BuildPackageId(preset.Name) : preset.Id.Trim();
            preset.Steps ??= new List<PresetProcessStep>();

            var existing = _installerService.GetAllPresets()
                .FirstOrDefault(p => string.Equals(p.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                _installerService.AddUserPreset(preset);
            else
                _installerService.UpdatePreset(preset);

            await LoadDataAsync();
            RebuildExecutionQueue();

            var availableIds = _installerService.GetAllAvailableProcesses()
                .Select(p => p.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingIds = preset.Steps
                .Where(step => !string.IsNullOrWhiteSpace(step.ProcessId) && !availableIds.Contains(step.ProcessId))
                .Select(step => step.ProcessId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingIds.Count > 0)
            {
                _log.Warning($"Package \"{preset.Name}\" imported with missing process references: {string.Join(", ", missingIds)}");
                OverallStatus = $"Pacchetto importato con {missingIds.Count} riferimento/i a processi mancanti.";
            }
            else
            {
                OverallStatus = $"Pacchetto importato: {preset.Name}";
            }
        }
        catch (Exception ex)
        {
            _log.Error("Import pacchetto fallito", ex);
            OverallStatus = $"Errore: import pacchetto fallito — {ex.Message}";
        }
    }

    private async Task ExportPackageAsync(PresetViewModel? presetVm)
    {
        if (presetVm is null) return;
        var preset = presetVm.Preset;
        if (preset is null) return;

        var safeName = string.Join("_", (preset.Name ?? "Pacchetto")
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Pacchetto";

        var dlg = new SaveFileDialog
        {
            Title = "Esporta pacchetto",
            Filter = "Pacchetto KlevaDeploy (*.kdp.package.json)|*.kdp.package.json|JSON (*.json)|*.json",
            FileName = $"{safeName}.kdp.package.json",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var includeProcesses = _dialogService.Confirm(
                "Esporta processi associati",
                "Vuoi includere nel file anche i processi associati al pacchetto?\n\nScegliendo \"No\" verrà esportato solo il pacchetto.");

            List<DeploymentProcess>? processes = null;
            if (includeProcesses)
            {
                var processIds = preset.Steps
                    .Select(step => step.ProcessId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                processes = _installerService.GetAllAvailableProcesses()
                    .Where(process => processIds.Contains(process.Id))
                    .Select(MaterializeExternalScriptsForExport)
                    .ToList();
            }

            var dto = new PackageBundleDto
            {
                SchemaVersion = 1,
                Package = preset,
                Processes = processes is { Count: > 0 } ? processes : null
            };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(dlg.FileName, json);
        }
        catch (Exception ex)
        {
            _log.Error("Export pacchetto fallito", ex);
            OverallStatus = $"Errore: export pacchetto fallito — {ex.Message}";
        }
    }

    private void ImportProcessLibrary()
    {
        try
        {
            CreateProcess();
            if (CreateProcessViewModel.ImportProcessCommand.CanExecute(null))
                CreateProcessViewModel.ImportProcessCommand.Execute(null);
        }
        catch (Exception ex)
        {
            _log.Error("Import processo fallito", ex);
            OverallStatus = $"Errore: import processo fallito — {ex.Message}";
        }
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

        UpdateExecutionIndices();
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
        RefreshPresetProcessAvailability(allAvailableProcesses);
        var availableProcessIds = allAvailableProcesses
            .Select(process => process.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Determine which processes are in selected presets
        HashSet<string> processesInSelectedPresets = new();
        Dictionary<string, int> processOrderInPreset = new();
        Dictionary<string, bool> processRequiredInPreset = new();
        if (selected.Count > 0)
        {
            foreach (var preset in selected)
            {
                var missing = preset.Steps
                    .Where(step => !string.IsNullOrWhiteSpace(step.ProcessId) && !availableProcessIds.Contains(step.ProcessId))
                    .Select(step => step.ProcessId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (missing.Count > 0)
                {
                    _log.Warning($"Package \"{preset.Name}\" contains missing process references ignored in queue: {string.Join(", ", missing)}");
                }
            }

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

            if (_prefsService.Preferences.ProcessOrderOverrides.TryGetValue(process.Id, out var overriddenOrder) && overriddenOrder > 0)
            {
                stepVm.Order = overriddenOrder;
            }
            
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
        _log.Info($"Execution queue rebuilt: {ExecutionQueue.Count} total processes ({processesInSelectedPresets.Count} in selected packages).");
        MoveQueueStepUpCommand.NotifyCanExecuteChanged();
        MoveQueueStepDownCommand.NotifyCanExecuteChanged();
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

        if (step.IsEnabled && step.Order >= 9999)
        {
            var max = ExecutionQueue
                .Where(s => s.IsEnabled && !ReferenceEquals(s, step))
                .Select(s => s.Order)
                .DefaultIfEmpty(0)
                .Max();

            var next = Math.Max(10, ((max / 10) + 1) * 10);
            step.Order = next;
            _prefsService.Preferences.ProcessOrderOverrides[step.Process.Id] = next;
            _prefsService.Save();
        }

        ResortExecutionQueue();
        ApplyProcessFilter();
        MoveQueueStepUpCommand.NotifyCanExecuteChanged();
        MoveQueueStepDownCommand.NotifyCanExecuteChanged();
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

    private bool CanMoveQueueStepUp(ProcessStepViewModel? step)
    {
        if (step is null) return false;
        if (!step.IsEnabled) return false;
        if (IsRunning || IsInitializing) return false;
        var enabled = ExecutionQueue.Where(s => s.IsEnabled).OrderBy(s => s.Order).ToList();
        var idx = enabled.IndexOf(step);
        return idx > 0;
    }

    private bool CanMoveQueueStepDown(ProcessStepViewModel? step)
    {
        if (step is null) return false;
        if (!step.IsEnabled) return false;
        if (IsRunning || IsInitializing) return false;
        var enabled = ExecutionQueue.Where(s => s.IsEnabled).OrderBy(s => s.Order).ToList();
        var idx = enabled.IndexOf(step);
        return idx >= 0 && idx < enabled.Count - 1;
    }

    private void MoveQueueStepUp(ProcessStepViewModel? step) => MoveQueueStep(step, -1);
    private void MoveQueueStepDown(ProcessStepViewModel? step) => MoveQueueStep(step, +1);

    private void MoveQueueStep(ProcessStepViewModel? step, int delta)
    {
        if (step is null) return;
        if (!step.IsEnabled) return;
        if (IsRunning || IsInitializing) return;

        var enabled = ExecutionQueue.Where(s => s.IsEnabled).OrderBy(s => s.Order).ToList();
        var idx = enabled.IndexOf(step);
        if (idx < 0) return;
        var target = idx + delta;
        if (target < 0 || target >= enabled.Count) return;

        (enabled[idx], enabled[target]) = (enabled[target], enabled[idx]);

        var order = 10;
        foreach (var s in enabled)
        {
            s.Order = order;
            _prefsService.Preferences.ProcessOrderOverrides[s.Process.Id] = order;
            order += 10;
        }
        _prefsService.Save();

        ResortExecutionQueue();
        ApplyProcessFilter();
        MoveQueueStepUpCommand.NotifyCanExecuteChanged();
        MoveQueueStepDownCommand.NotifyCanExecuteChanged();
    }

    private void UpdateExecutionIndices()
    {
        var i = 1;
        foreach (var step in ExecutionQueue.Where(s => s.IsEnabled).OrderBy(s => s.Order))
        {
            step.ExecutionIndex = i;
            i++;
        }

        foreach (var step in ExecutionQueue.Where(s => !s.IsEnabled))
        {
            step.ExecutionIndex = 0;
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

                RefreshPresetProcessAvailability();
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

            RefreshPresetProcessAvailability();
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
            _log.Info("Apertura pannello creazione pacchetto.");
        }
        catch (Exception ex)
        {
            _log.Error("Errore durante l'apertura del pannello creazione pacchetto", ex);
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
                _log.Error("Impossibile modificare il pacchetto: lista processi non disponibile.");
                return;
            }

            CreatePresetViewModel.InitializeForEdit(presetVm.Preset, allProcesses);
            IsCreatePresetPanelOpen = true;
            _log.Info($"Apertura pannello modifica pacchetto: {presetVm.Name}");
        }
        catch (Exception ex)
        {
            _log.Error($"Errore durante l'apertura della modifica per il pacchetto {presetVm?.Name}", ex);
        }
    }

    private void DeletePreset(PresetViewModel? presetVm)
    {
        try
        {
            if (presetVm is null) return;

            var confirmed = _dialogService.Confirm(
                "Elimina pacchetto",
                $"Sei sicuro di voler eliminare il pacchetto \"{presetVm.Name}\"?");
            if (!confirmed) return;

            var deleted = _installerService.DeletePreset(presetVm.Preset.Id);
            if (!deleted)
            {
                _log.Error($"Impossibile eliminare il pacchetto: {presetVm.Name}");
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
            _log.Error("Errore durante l'eliminazione del pacchetto", ex);
        }
    }

    private void DeleteProcess(ProcessStepViewModel? stepVm)
    {
        try
        {
            if (stepVm is null) return;

            var confirmed = _dialogService.Confirm(
                "Elimina processo",
                $"Sei sicuro di voler eliminare il processo \"{stepVm.Name}\"?\n\nQuesta operazione influirà su tutti i pacchetti che lo utilizzano.");
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
                    RefreshPresetProcessAvailability();
                    ApplyPresetFilter();
                    
                    if (wasSelected)
                    {
                        RebuildExecutionQueue();
                    }
                    
                    _log.Info($"Updated package: {CreatePresetViewModel.CreatedPreset.Name}");
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
                RefreshPresetProcessAvailability();
                ApplyPresetFilter();
                
                _log.Info($"Created new package: {CreatePresetViewModel.CreatedPreset.Name}");
            }
        }
        else
        {
            _log.Info("Package operation cancelled.");
        }
    }

    private void OnCreatePresetDeleteRequested(object? sender, EventArgs e)
    {
        try
        {
            var vm = CreatePresetViewModel;
            if (!vm.IsEditMode) return;

            var confirmed = _dialogService.Confirm(
                "Elimina pacchetto",
                $"Sei sicuro di voler eliminare il pacchetto \"{vm.Name}\"?");
            if (!confirmed) return;

            var deleted = _installerService.DeletePreset(vm.PresetId);
            if (!deleted)
            {
                vm.ValidationError = "Impossibile eliminare questo pacchetto.";
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
            _log.Error("Errore durante l'eliminazione del pacchetto", ex);
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
        _argumentPromptChoiceThisRun.Clear();

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
            UpdateArgumentProfileAfterRun(process, result.ExitCode == 0 || result.ExitCode == 3010 || result.ExitCode == 1641);
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
        var enabledSteps = ExecutionQueue.Where(s => s.IsEnabled).OrderBy(s => s.Order).ToList();
        if (enabledSteps.Count == 0) return;

        ResetQueueVisuals();
        _argumentPromptChoiceThisRun.Clear();

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
        IsTerminalTabSelected = false;
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
                UpdateArgumentProfileAfterRun(process, result.ExitCode == 0 || result.ExitCode == 3010 || result.ExitCode == 1641);

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
        IDisposable? argScope = null;
        try
        {
        var installDir = NormalizeInstallDirectory(process.InstallDirectory);
        var rawArgs = (arguments ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(rawArgs) && rawArgs.Contains("{INSTALL_DIR}", StringComparison.OrdinalIgnoreCase))
            rawArgs = rawArgs.Replace("{INSTALL_DIR}", installDir ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var expandedArguments = string.Empty;

        if (process.SubProcesses is not null && process.SubProcesses.Count > 0)
        {
            var ensured = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: null, msiPath: null, ct);
            if (ensured.EarlyReturn is not null)
                return ensured.EarlyReturn;
            argScope = ensured.Scope;

            var extractionMode = process.Kind == ProcessKind.Installer ? GetExeExtractionMode(rawArgs) : ExeExtractionMode.None;
            var argsForChildren = extractionMode == ExeExtractionMode.None ? rawArgs : StripExeExecutionMarkers(rawArgs);
            expandedArguments = Environment.ExpandEnvironmentVariables(argsForChildren);

            var parentInstallerPath = string.IsNullOrWhiteSpace(process.RelativePath)
                ? string.Empty
                : (Path.IsPathRooted(process.RelativePath)
                    ? process.RelativePath
                    : Path.Combine(AppContext.BaseDirectory, process.RelativePath));

            if (process.Kind == ProcessKind.Installer &&
                process.InstallerSourceMode != InstallerSourceMode.StaticLocal)
            {
                if (string.IsNullOrWhiteSpace(parentInstallerPath) || !File.Exists(parentInstallerPath))
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

                    parentInstallerPath = string.IsNullOrWhiteSpace(process.RelativePath)
                        ? string.Empty
                        : (Path.IsPathRooted(process.RelativePath)
                            ? process.RelativePath
                            : Path.Combine(AppContext.BaseDirectory, process.RelativePath));

                    if (string.IsNullOrWhiteSpace(parentInstallerPath) || !File.Exists(parentInstallerPath))
                    {
                        var src = process.InstallerSourceMode == InstallerSourceMode.StaticWeb
                            ? (string.IsNullOrWhiteSpace(process.DownloadUrl) ? "URL non impostato" : process.DownloadUrl.Trim())
                            : (string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl) ? "Cartella web non impostata" : process.DownloadBaseFolderUrl.Trim());

                        var err = $"Installer non trovato prima dei sottoprocessi. Sorgente: {src}";
                        if (!string.IsNullOrWhiteSpace(downloadError))
                            err += $" — {downloadError}";
                        step.SetStatus("❌", "Download fallito");
                        return new ProcessResult(1, string.Empty, err);
                    }
                }
            }
            else if (process.Kind == ProcessKind.Installer)
            {
                if (string.IsNullOrWhiteSpace(parentInstallerPath) || !File.Exists(parentInstallerPath))
                {
                    var err = $"Installer non trovato prima dei sottoprocessi: {parentInstallerPath}";
                    step.SetStatus("❌", "Installer non trovato");
                    return new ProcessResult(1, string.Empty, err);
                }
            }

            if (process.Kind == ProcessKind.Installer &&
                extractionMode != ExeExtractionMode.None)
            {
                var ext = Path.GetExtension(parentInstallerPath).ToLowerInvariant();
                if (ext == ".exe")
                {
                    step.SetStatus("⏳", "Extracting installer...");
                    var extracted = await ExtractBootstrapperAsync(step, process, parentInstallerPath, ct);

                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["KLEVADEPLOY_EXTRACT_DIR"] = extracted.ExtractedDir,
                        ["KLEVADEPLOY_EXTRACT_MAIN_INSTALLER"] = extracted.MainInstallerPath,
                        ["KLEVADEPLOY_EXTRACT_ALL_MSIS"] = extracted.AllMsis.Length == 0 ? string.Empty : string.Join(';', extracted.AllMsis),
                        ["KLEVADEPLOY_EXTRACT_MODE"] = extractionMode.ToString()
                    };

                    argScope = new CompositeScope(argScope, new EnvironmentVariableScope(values));
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
                step.IsSubProcessRunning = true;
                step.IsSubProgressIndeterminate = true;
                step.SubProgressValue = 0;
                step.CurrentSubProcessName = stepName;

                try
                {
                    last = await RunDeploymentProcessAsync(step, spProcess, subArgs, ct);
                }
                finally
                {
                    step.IsSubProcessRunning = false;
                    step.IsSubProgressIndeterminate = false;
                    step.SubProgressValue = 0;
                    step.CurrentSubProcessName = string.Empty;
                }
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
                {
                    if (process.InstallerSourceMode == InstallerSourceMode.StaticLocal)
                        throw new InvalidOperationException($"Installer path missing for process: {process.Name}");
                    process.RelativePath = string.Empty;
                }

                var installerPath = string.IsNullOrWhiteSpace(process.RelativePath)
                    ? string.Empty
                    : (Path.IsPathRooted(process.RelativePath)
                        ? process.RelativePath
                        : Path.Combine(AppContext.BaseDirectory, process.RelativePath));

                var shouldAutoUpdateBeforeRun =
                    process.InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
                    process.DownloadUseLatestVersion;

                if (process.InstallerSourceMode != InstallerSourceMode.StaticLocal &&
                    (shouldAutoUpdateBeforeRun || string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath)))
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

                    installerPath = string.IsNullOrWhiteSpace(process.RelativePath)
                        ? string.Empty
                        : (Path.IsPathRooted(process.RelativePath)
                            ? process.RelativePath
                            : Path.Combine(AppContext.BaseDirectory, process.RelativePath));

                    if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
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
                var extractionMode = GetExeExtractionMode(rawArgs);
                if (ext == ".exe" && extractionMode != ExeExtractionMode.None)
                {
                    rawArgs = StripExeExecutionMarkers(rawArgs);

                    var ensuredExtracted = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: installerPath, msiPath: null, ct);
                    if (ensuredExtracted.EarlyReturn is not null)
                        return ensuredExtracted.EarlyReturn;
                    argScope = ensuredExtracted.Scope;

                    expandedArguments = Environment.ExpandEnvironmentVariables(rawArgs);
                    return await RunExtractedBootstrapperAsync(step, process, installerPath, expandedArguments, installDir, extractionMode, ct);
                }
                if (ext != ".exe" && (rawArgs.Contains("{AUTO}", StringComparison.OrdinalIgnoreCase) ||
                                      rawArgs.Contains("{SILENT}", StringComparison.OrdinalIgnoreCase) ||
                                      rawArgs.Contains("{AUTOEXTRACT_MAIN_MSI}", StringComparison.OrdinalIgnoreCase) ||
                                      rawArgs.Contains("{AUTOEXTRACT_ALL_MSI}", StringComparison.OrdinalIgnoreCase)))
                {
                    rawArgs = StripExeExecutionMarkers(rawArgs);
                }
                if (ext == ".exe" && rawArgs.Contains("{AUTO}", StringComparison.OrdinalIgnoreCase))
                {
                    var family = ExeInstallerAnalysis.TryDetectExeInstallerFamily(installerPath);
                    var argsNoMarkers = StripExeExecutionMarkers(rawArgs);

                    if (string.Equals(family, "WiX Burn", StringComparison.OrdinalIgnoreCase))
                    {
                        var (layoutDir, extractedMsiFromBurn) = await TryExtractMsiFromBurnBundleAsync(installerPath, ct);
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(extractedMsiFromBurn) && File.Exists(extractedMsiFromBurn))
                            {
                                var ensuredMsi = await EnsureArgumentInputsAsync(step, process, argsNoMarkers, installerArtifactPath: installerPath, msiPath: extractedMsiFromBurn, ct);
                                if (ensuredMsi.EarlyReturn is not null)
                                    return ensuredMsi.EarlyReturn;
                                argScope = ensuredMsi.Scope;

                                expandedArguments = Environment.ExpandEnvironmentVariables(argsNoMarkers);
                                var msiArgs = string.IsNullOrWhiteSpace(installDir)
                                    ? expandedArguments
                                    : AppendMsiInstallDirIfMissing(expandedArguments, installDir);
                                return await RunMsiWithAppProgressAsync(step, extractedMsiFromBurn, msiArgs, process.RunAsAdmin, ct);
                            }
                        }
                        finally
                        {
                            if (!string.IsNullOrWhiteSpace(layoutDir))
                                TryDeleteDirectory(layoutDir);
                        }
                    }

                    try
                    {
                        var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync(ct);
                        var listArgs = $"l -slt \"{installerPath}\"";
                        var listResult = await _processExecutionService.RunAsync(sevenZipExe, listArgs, runAsAdmin: false, ct);
                        if (listResult.ExitCode == 0)
                        {
                            var msiCount = ExeInstallerAnalysis.CountMsiPathsFrom7ZipSlt(listResult.StdOut);
                            if (msiCount > 0)
                            {
                                var mode = msiCount > 1 ? ExeExtractionMode.AllMsis : ExeExtractionMode.MainMsiOnly;

                                var ensuredExtracted = await EnsureArgumentInputsAsync(step, process, argsNoMarkers, installerArtifactPath: installerPath, msiPath: null, ct);
                                if (ensuredExtracted.EarlyReturn is not null)
                                    return ensuredExtracted.EarlyReturn;
                                argScope = ensuredExtracted.Scope;

                                expandedArguments = Environment.ExpandEnvironmentVariables(argsNoMarkers);
                                return await RunExtractedBootstrapperAsync(step, process, installerPath, expandedArguments, installDir, mode, ct);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warning($"Auto mode: 7-Zip MSI probe failed for {Path.GetFileName(installerPath)}: {ex.Message}");
                    }

                    if (TryResolveSilentArgsForExeInstallerKnown(installerPath, out var silent))
                        rawArgs = rawArgs.Replace("{AUTO}", silent, StringComparison.OrdinalIgnoreCase).Trim();
                    else
                    {
                        rawArgs = rawArgs.Replace("{AUTO}", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                        _log.Warning($"Auto mode: no MSI detected and EXE family not recognized, running as manual: {Path.GetFileName(installerPath)}");
                    }
                }
                if (ext == ".exe" && rawArgs.Contains("{SILENT}", StringComparison.OrdinalIgnoreCase))
                {
                    var silent = ResolveSilentArgsForExeInstaller(installerPath);
                    rawArgs = rawArgs.Replace("{SILENT}", silent, StringComparison.OrdinalIgnoreCase).Trim();
                }
                if (ext == ".msi")
                {
                    var ensuredMsi = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: installerPath, msiPath: installerPath, ct);
                    if (ensuredMsi.EarlyReturn is not null)
                        return ensuredMsi.EarlyReturn;
                    argScope = ensuredMsi.Scope;

                    expandedArguments = Environment.ExpandEnvironmentVariables(rawArgs);

                    var msiArgs = string.IsNullOrWhiteSpace(installDir)
                        ? expandedArguments
                        : AppendMsiInstallDirIfMissing(expandedArguments, installDir);
                    return await RunMsiWithAppProgressAsync(step, installerPath, msiArgs, process.RunAsAdmin, ct);
                }

                var ensured = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: installerPath, msiPath: null, ct);
                if (ensured.EarlyReturn is not null)
                    return ensured.EarlyReturn;
                argScope = ensured.Scope;

                expandedArguments = Environment.ExpandEnvironmentVariables(rawArgs);
                return await _processExecutionService.RunAsync(installerPath, expandedArguments, process.RunAsAdmin, ct);
            }
            case ProcessKind.PowerShellScript:
            {
                var ensured = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: null, msiPath: null, ct);
                if (ensured.EarlyReturn is not null)
                    return ensured.EarlyReturn;
                argScope = ensured.Scope;

                EventHandler<LogEntry>? progressHandler = null;
                if (step.IsSubProcessRunning)
                {
                    var gate = new object();
                    var lastPercent = -1;
                    progressHandler = async (_, entry) =>
                    {
                        if (!string.Equals(entry.Level, "STDOUT", StringComparison.OrdinalIgnoreCase)) return;
                        var msg = (entry.Message ?? string.Empty).Trim();
                        if (!msg.StartsWith("KLEVADEPLOY_PROGRESS:", StringComparison.OrdinalIgnoreCase)) return;

                        var tail = msg["KLEVADEPLOY_PROGRESS:".Length..].Trim();
                        if (!int.TryParse(tail, out var p)) return;
                        if (p < 0) p = 0;
                        if (p > 100) p = 100;

                        var shouldUpdate = false;
                        lock (gate)
                        {
                            if (p > lastPercent)
                            {
                                lastPercent = p;
                                shouldUpdate = true;
                            }
                        }
                        if (!shouldUpdate) return;

                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            step.IsSubProgressIndeterminate = false;
                            step.SubProgressValue = p;
                        });
                    };
                    _log.LogAdded += progressHandler;
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                    {
                        return await _processExecutionService.RunPowerShellAsync(process.ScriptContent, isInlineScript: true, process.RunAsAdmin, ct);
                    }

                    if (string.IsNullOrWhiteSpace(process.RelativePath))
                        throw new InvalidOperationException($"PowerShell script path missing for process: {process.Name}");

                    var scriptPath = ResolveExecutableResourcePath(process.RelativePath);
                    return await _processExecutionService.RunPowerShellAsync(scriptPath, isInlineScript: false, process.RunAsAdmin, ct);
                }
                finally
                {
                    if (progressHandler is not null)
                        _log.LogAdded -= progressHandler;
                }
            }
            case ProcessKind.BatchScript:
            {
                var ensured = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: null, msiPath: null, ct);
                if (ensured.EarlyReturn is not null)
                    return ensured.EarlyReturn;
                argScope = ensured.Scope;

                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunBatchAsync(process.ScriptContent, isInlineScript: true, process.RunAsAdmin, ct);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Batch script path missing for process: {process.Name}");

                var scriptPath = ResolveExecutableResourcePath(process.RelativePath);
                return await _processExecutionService.RunBatchAsync(scriptPath, isInlineScript: false, process.RunAsAdmin, ct);
            }
            case ProcessKind.BashScript:
            {
                var ensured = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: null, msiPath: null, ct);
                if (ensured.EarlyReturn is not null)
                    return ensured.EarlyReturn;
                argScope = ensured.Scope;

                if (!string.IsNullOrWhiteSpace(process.ScriptContent))
                {
                    return await _processExecutionService.RunBashAsync(process.ScriptContent, isInlineScript: true, ct);
                }

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Bash script path missing for process: {process.Name}");

                var scriptPath = ResolveExecutableResourcePath(process.RelativePath);
                return await _processExecutionService.RunBashAsync(scriptPath, isInlineScript: false, ct);
            }
            case ProcessKind.RegistryFile:
            {
                var ensured = await EnsureArgumentInputsAsync(step, process, rawArgs, installerArtifactPath: null, msiPath: null, ct);
                if (ensured.EarlyReturn is not null)
                    return ensured.EarlyReturn;
                argScope = ensured.Scope;

                if (string.IsNullOrWhiteSpace(process.RelativePath))
                    throw new InvalidOperationException($"Registry file path missing for process: {process.Name}");

                var regPath = ResolveExecutableResourcePath(process.RelativePath);
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
        finally
        {
            argScope?.Dispose();
        }
    }

    private async Task<(IDisposable? Scope, ProcessResult? EarlyReturn)> EnsureArgumentInputsAsync(
        ProcessStepViewModel step,
        DeploymentProcess process,
        string rawArgs,
        string? installerArtifactPath,
        string? msiPath,
        System.Threading.CancellationToken ct)
    {
        var inputs = new List<ArgumentInputDefinition>();
        if (process.ArgumentInputs is not null && process.ArgumentInputs.Count > 0)
        {
            inputs.AddRange(
                process.ArgumentInputs
                    .Where(x => !string.IsNullOrWhiteSpace((x.Key ?? string.Empty).Trim()))
                    .Select(x => new ArgumentInputDefinition
                    {
                        Key = (x.Key ?? string.Empty).Trim(),
                        Label = x.Label ?? string.Empty,
                        Description = x.Description ?? string.Empty,
                        DefaultValue = x.DefaultValue ?? string.Empty,
                        IsSecret = x.IsSecret,
                        IsRequired = x.IsRequired
                    }));
        }

        var inferred = InferArgumentInputsFromArguments(rawArgs);
        if (inferred.Count > 0)
        {
            var existingKeys = new HashSet<string>(inputs.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var d in inferred)
            {
                if (existingKeys.Contains(d.Key)) continue;
                inputs.Add(d);
            }
        }

        if (inputs.Count == 0)
            return (null, null);

        var prefs = _prefsService.Preferences;
        prefs.ProcessArgumentProfiles ??= new List<ProcessArgumentProfile>();

        var processKey = GetArgumentProfileProcessKey(process);
        var schemaHash = ComputeArgumentInputsSchemaHash(inputs);

        var profile = prefs.ProcessArgumentProfiles
            .FirstOrDefault(p => string.Equals((p.ProcessId ?? string.Empty).Trim(), processKey, StringComparison.OrdinalIgnoreCase));

        static bool IsMissingRequired(ProcessArgumentProfile? p, IReadOnlyList<ArgumentInputDefinition> defs)
        {
            foreach (var d in defs)
            {
                if (!d.IsRequired) continue;
                if (p?.Values is null) return true;
                if (!p.Values.TryGetValue(d.Key, out var v) || string.IsNullOrWhiteSpace(v))
                    return true;
            }
            return false;
        }

        var schemaMismatch = profile is null || !string.Equals(profile.SchemaHash ?? string.Empty, schemaHash, StringComparison.OrdinalIgnoreCase);
        var shouldPrompt = profile is null || schemaMismatch || profile.LastRunFailed || IsMissingRequired(profile, inputs);

        var mergedPrefill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in inputs)
        {
            mergedPrefill[d.Key] = d.DefaultValue ?? string.Empty;
        }

        if (profile?.Values is not null)
        {
            foreach (var kvp in profile.Values)
            {
                var k = (kvp.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(k)) continue;
                if (!mergedPrefill.ContainsKey(k)) continue;
                mergedPrefill[k] = kvp.Value ?? string.Empty;
            }
        }

        if (!shouldPrompt)
        {
            _argumentPromptChoiceThisRun[processKey] = ArgumentPromptChoice.RunAlways;
            return (new EnvironmentVariableScope(mergedPrefill), null);
        }

        string? prefillNotice = null;
        var hasAnyEmpty = mergedPrefill.Values.Any(v => string.IsNullOrWhiteSpace(v));
        if (hasAnyEmpty)
        {
            var defaults = await TryGetDefaultsFromInstallerAsync(installerArtifactPath, msiPath, rawArgs, ct);
            foreach (var kvp in defaults.Defaults)
            {
                if (!mergedPrefill.TryGetValue(kvp.Key, out var cur) || !string.IsNullOrWhiteSpace(cur))
                    continue;
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                    mergedPrefill[kvp.Key] = kvp.Value;
            }
            prefillNotice = defaults.Notice;
        }

        ArgumentPromptResponse response;
        var subtitle = schemaMismatch
            ? "Parametri aggiornati: verifica e aggiorna i valori necessari."
            : "Inserisci i parametri richiesti per questo processo.";
        if (!string.IsNullOrWhiteSpace(prefillNotice))
            subtitle = $"{subtitle}\n\n{prefillNotice}".Trim();

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is not null && !app.Dispatcher.CheckAccess())
        {
            response = app.Dispatcher.Invoke(() =>
                _dialogService.ShowArgumentPrompt(process.Name, subtitle, inputs, mergedPrefill));
        }
        else
        {
            response = _dialogService.ShowArgumentPrompt(process.Name, subtitle, inputs, mergedPrefill);
        }

        _argumentPromptChoiceThisRun[processKey] = response.Choice;

        if (response.Choice == ArgumentPromptChoice.Cancel)
        {
            step.SetStatus("⛔", "Annullato");
            return (null, new ProcessResult(1, string.Empty, "Operazione annullata: parametri richiesti non forniti."));
        }

        var chosenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in inputs)
        {
            var v = string.Empty;
            if (response.Values is not null && response.Values.TryGetValue(d.Key, out var rv))
                v = rv ?? string.Empty;
            else if (mergedPrefill.TryGetValue(d.Key, out var pv))
                v = pv ?? string.Empty;

            chosenValues[d.Key] = v;
        }

        if (response.Choice == ArgumentPromptChoice.RunAlways)
        {
            if (profile is null)
            {
                profile = new ProcessArgumentProfile
                {
                    ProcessId = processKey,
                    SchemaHash = schemaHash,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    LastRunFailed = false
                };
                prefs.ProcessArgumentProfiles.Add(profile);
            }

            profile.SchemaHash = schemaHash;
            profile.Values = new Dictionary<string, string>(chosenValues, StringComparer.OrdinalIgnoreCase);
            profile.LastRunFailed = false;
            _prefsService.Save();
        }

        return (new EnvironmentVariableScope(chosenValues), null);
    }

    private static List<ArgumentInputDefinition> InferArgumentInputsFromArguments(string? arguments)
    {
        var text = arguments ?? string.Empty;
        if (text.Length == 0) return new List<ArgumentInputDefinition>();

        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        static bool LooksSecret(string key) =>
            key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("PASS", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("PWD", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("SECRET", StringComparison.OrdinalIgnoreCase);

        var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < text.Length - 2; i++)
        {
            if (text[i] != '%') continue;
            var end = text.IndexOf('%', i + 1);
            if (end <= i + 1) continue;

            var name = text.Substring(i + 1, end - i - 1).Trim();
            if (name.Length == 0) { i = end; continue; }
            if (!name.StartsWith("KLEVADEPLOY_", StringComparison.OrdinalIgnoreCase)) { i = end; continue; }
            if (!IsNameChar(name[0]) || char.IsDigit(name[0])) { i = end; continue; }
            var ok = true;
            for (var k = 1; k < name.Length; k++)
            {
                if (!IsNameChar(name[k])) { ok = false; break; }
            }
            if (!ok) { i = end; continue; }

            vars.Add(name);
            i = end;
        }

        var envToArgKey = InferEnvVarToMsiPropertyMap(text);

        var result = new List<ArgumentInputDefinition>();
        foreach (var v in vars.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var isSecret = LooksSecret(v);
            var envDefault = isSecret ? string.Empty : (Environment.GetEnvironmentVariable(v) ?? string.Empty);

            var label = v;
            var description = "Valore richiesto per completare l'installazione.";
            if (envToArgKey.TryGetValue(v, out var argKey) && !string.IsNullOrWhiteSpace(argKey))
            {
                label = argKey;
                description = $"Valore per '{argKey}'.";
            }

            result.Add(new ArgumentInputDefinition
            {
                Key = v,
                Label = label,
                Description = description,
                DefaultValue = envDefault,
                IsSecret = isSecret,
                IsRequired = true
            });
        }

        return result;
    }

    private sealed record InstallerDefaultsResult(IReadOnlyDictionary<string, string> Defaults, string? Notice);

    private async Task<InstallerDefaultsResult> TryGetDefaultsFromInstallerAsync(
        string? installerArtifactPath,
        string? msiPath,
        string rawArgs,
        System.Threading.CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(msiPath) && File.Exists(msiPath))
        {
            var msiEnvDefaults = TryGetEnvVarDefaultsFromMsi(msiPath, rawArgs);
            return msiEnvDefaults.Count == 0
                ? new InstallerDefaultsResult(msiEnvDefaults, "Impossibile leggere in modo automatico i valori predefiniti dall'MSI: verifica i campi.")
                : new InstallerDefaultsResult(msiEnvDefaults, "Valori precompilati letti automaticamente dall'installer.");
        }

        if (string.IsNullOrWhiteSpace(installerArtifactPath) || !File.Exists(installerArtifactPath))
            return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null);

        var ext = Path.GetExtension(installerArtifactPath).ToLowerInvariant();
        if (ext != ".exe")
            return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), null);

        try
        {
            var family = ExeInstallerAnalysis.TryDetectExeInstallerFamily(installerArtifactPath);

            if (string.Equals(family, "WiX Burn", StringComparison.OrdinalIgnoreCase))
            {
                var (layoutDir, extractedMsiFromBurn) = await TryExtractMsiFromBurnBundleAsync(installerArtifactPath, ct);
                try
                {
                    if (!string.IsNullOrWhiteSpace(extractedMsiFromBurn) && File.Exists(extractedMsiFromBurn))
                    {
                        var burnDefaults = TryGetEnvVarDefaultsFromMsi(extractedMsiFromBurn, rawArgs);
                        if (burnDefaults.Count > 0)
                            return new InstallerDefaultsResult(burnDefaults, "Valori precompilati letti automaticamente dall'installer.");
                    }
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(layoutDir))
                        TryDeleteDirectory(layoutDir);
                }

                return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), BuildUnsupportedInstallerNotice(installerArtifactPath));
            }

            var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync(ct);
            var listArgs = $"l -slt \"{installerArtifactPath}\"";
            var listResult = await _processExecutionService.RunAsync(sevenZipExe, listArgs, runAsAdmin: false, ct);
            if (listResult.ExitCode != 0)
                return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), BuildUnsupportedInstallerNotice(installerArtifactPath));

            var msiInArchive = ParseFirstMsiPathFrom7ZipSlt(listResult.StdOut);
            if (string.IsNullOrWhiteSpace(msiInArchive))
                return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), BuildUnsupportedInstallerNotice(installerArtifactPath));

            var tempDir = Path.Combine(AppContext.BaseDirectory, "Data", "temp", "prefill", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var extractArgs = $"e \"{installerArtifactPath}\" -o\"{tempDir}\" \"{msiInArchive}\" -y";
            var extractResult = await _processExecutionService.RunAsync(sevenZipExe, extractArgs, runAsAdmin: false, ct);
            if (extractResult.ExitCode != 0)
            {
                TryDeleteDirectory(tempDir);
                return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), BuildUnsupportedInstallerNotice(installerArtifactPath));
            }

            var extractedMsi = Directory.EnumerateFiles(tempDir, "*.msi", SearchOption.AllDirectories).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(extractedMsi) || !File.Exists(extractedMsi))
            {
                TryDeleteDirectory(tempDir);
                return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), BuildUnsupportedInstallerNotice(installerArtifactPath));
            }

            var defaults = TryGetEnvVarDefaultsFromMsi(extractedMsi, rawArgs);
            TryDeleteDirectory(tempDir);
            if (defaults.Count == 0)
                return new InstallerDefaultsResult(defaults, BuildUnsupportedInstallerNotice(installerArtifactPath));

            return new InstallerDefaultsResult(defaults, "Valori precompilati letti automaticamente dall'installer.");
        }
        catch
        {
            return new InstallerDefaultsResult(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), BuildUnsupportedInstallerNotice(installerArtifactPath));
        }
    }

    private async Task<(string? LayoutDir, string? ExtractedMsi)> TryExtractMsiFromBurnBundleAsync(string bundleExePath, System.Threading.CancellationToken ct)
    {
        var layoutDir = Path.Combine(AppContext.BaseDirectory, "Data", "temp", "prefill", "burn-layout", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(layoutDir);
        }
        catch
        {
            return (null, null);
        }

        try
        {
            var args = $"/layout \"{layoutDir}\" /quiet";
            var result = await _processExecutionService.RunAsync(bundleExePath, args, runAsAdmin: false, ct);
            var ok = result.ExitCode == 0 || result.ExitCode == 3010 || result.ExitCode == 1641;
            if (!ok)
                return (layoutDir, null);

            var msi = Directory
                .EnumerateFiles(layoutDir, "*.msi", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return (layoutDir, msi);
        }
        catch
        {
            return (layoutDir, null);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static string? ParseFirstMsiPathFrom7ZipSlt(string? stdout)
    {
        var text = stdout ?? string.Empty;
        if (text.Length == 0) return null;

        string? currentPath = null;
        foreach (var raw in SplitLines(text))
        {
            var line = raw.Trim();
            if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
            {
                currentPath = line["Path = ".Length..].Trim();
                continue;
            }
            if (line.StartsWith("Attributes = ", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(currentPath) &&
                    currentPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    return currentPath;
                }
            }
        }

        return null;
    }

    private static string BuildUnsupportedInstallerNotice(string installerPath)
    {
        var kind = ExeInstallerAnalysis.TryDetectExeInstallerFamily(installerPath);
        if (!string.IsNullOrWhiteSpace(kind))
            return $"Tipo installer rilevato: {kind}. Impossibile leggere in modo automatico i valori predefiniti: verifica i campi.";
        return "Impossibile leggere in modo automatico i valori predefiniti dell'installer: verifica i campi.";
    }

    private static ExeExtractionMode GetExeExtractionMode(string rawArgs)
    {
        var text = rawArgs ?? string.Empty;
        if (text.Contains("{AUTOEXTRACT_ALL_MSI}", StringComparison.OrdinalIgnoreCase))
            return ExeExtractionMode.AllMsis;
        if (text.Contains("{AUTOEXTRACT_MAIN_MSI}", StringComparison.OrdinalIgnoreCase))
            return ExeExtractionMode.MainMsiOnly;
        return ExeExtractionMode.None;
    }

    private static string StripExeExecutionMarkers(string rawArgs)
    {
        var text = rawArgs ?? string.Empty;
        text = text.Replace("{AUTO}", string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{SILENT}", string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{AUTOEXTRACT_ALL_MSI}", string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{AUTOEXTRACT_MAIN_MSI}", string.Empty, StringComparison.OrdinalIgnoreCase);
        return text.Trim();
    }

    private static string ResolveSilentArgsForExeInstaller(string installerPath)
    {
        var kind = ExeInstallerAnalysis.TryDetectExeInstallerFamily(installerPath);
        if (string.Equals(kind, "Inno Setup", StringComparison.OrdinalIgnoreCase))
            return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
        if (string.Equals(kind, "NSIS", StringComparison.OrdinalIgnoreCase))
            return "/S";
        if (string.Equals(kind, "InstallShield", StringComparison.OrdinalIgnoreCase))
            return "/s /v\"/qn /norestart\"";
        if (string.Equals(kind, "WiX Burn", StringComparison.OrdinalIgnoreCase))
            return "/quiet /norestart";
        return "/quiet /norestart";
    }

    private static bool TryResolveSilentArgsForExeInstallerKnown(string installerPath, out string args)
    {
        var kind = ExeInstallerAnalysis.TryDetectExeInstallerFamily(installerPath);
        if (string.Equals(kind, "Inno Setup", StringComparison.OrdinalIgnoreCase))
        {
            args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART";
            return true;
        }
        if (string.Equals(kind, "NSIS", StringComparison.OrdinalIgnoreCase))
        {
            args = "/S";
            return true;
        }
        if (string.Equals(kind, "InstallShield", StringComparison.OrdinalIgnoreCase))
        {
            args = "/s /v\"/qn /norestart\"";
            return true;
        }
        if (string.Equals(kind, "WiX Burn", StringComparison.OrdinalIgnoreCase))
        {
            args = "/quiet /norestart";
            return true;
        }

        args = string.Empty;
        return false;
    }

    private sealed record BootstrapperExtractionResult(string ExtractedDir, string MainInstallerPath, string[] AllMsis);

    private async Task<BootstrapperExtractionResult> ExtractBootstrapperAsync(
        ProcessStepViewModel step,
        DeploymentProcess process,
        string installerPath,
        System.Threading.CancellationToken ct)
    {
        var extractedDir = CreateProcessExtractionDir(process.Name);

        step.SetStatus("⏳", "Downloading 7-Zip...");
        var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync(ct);

        var extractArgs = $"x \"{installerPath}\" -o\"{extractedDir}\" -y";
        var extractResult = await _processExecutionService.RunAsync(sevenZipExe, extractArgs, runAsAdmin: false, ct);
        if (extractResult.ExitCode != 0)
            throw new InvalidOperationException($"7-Zip extraction failed (exit {extractResult.ExitCode}).");

        var mainInstaller = ResolveInstallerFromExtractedDir(extractedDir);
        if (string.IsNullOrWhiteSpace(mainInstaller) || !File.Exists(mainInstaller))
            throw new InvalidOperationException($"Installer non trovato dopo l'estrazione: {installerPath}");

        var allMsis = FindExtractedMsis(extractedDir).ToArray();
        return new BootstrapperExtractionResult(extractedDir, mainInstaller, allMsis);
    }

    private async Task<ProcessResult> RunExtractedBootstrapperAsync(
        ProcessStepViewModel step,
        DeploymentProcess process,
        string installerPath,
        string expandedArguments,
        string? installDir,
        ExeExtractionMode extractionMode,
        System.Threading.CancellationToken ct)
    {
        var extractedDir = CreateProcessExtractionDir(process.Name);
        var msiLogDir = Path.Combine(AppContext.BaseDirectory, "Data", "msi-logs");
        Directory.CreateDirectory(msiLogDir);

        step.IsProgressIndeterminate = false;
        step.ProgressValue = 0;
        step.SetStatus("⏳", "Preparing...");

        step.SetStatus("⏳", "Downloading 7-Zip...");
        step.ProgressValue = 10;
        var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync(ct);

        step.SetStatus("⏳", "Extracting...");
        step.ProgressValue = 40;
        var extractArgs = $"x \"{installerPath}\" -o\"{extractedDir}\" -y";
        var extractResult = await _processExecutionService.RunAsync(sevenZipExe, extractArgs, runAsAdmin: false, ct);
        if (extractResult.ExitCode != 0)
            return new ProcessResult(extractResult.ExitCode, string.Empty, $"7-Zip extraction failed (exit {extractResult.ExitCode}).");

        var mainInstaller = ResolveInstallerFromExtractedDir(extractedDir);
        if (string.IsNullOrWhiteSpace(mainInstaller) || !File.Exists(mainInstaller))
            throw new InvalidOperationException($"Installer non trovato dopo l'estrazione: {installerPath}");

        var mainExt = Path.GetExtension(mainInstaller).ToLowerInvariant();
        if (mainExt == ".msi" && extractionMode == ExeExtractionMode.AllMsis)
        {
            step.SetStatus("⏳", "Installing prerequisites...");
            step.ProgressValue = 65;

            foreach (var prereqMsi in FindExtractedMsis(extractedDir))
            {
                ct.ThrowIfCancellationRequested();
                if (string.Equals(prereqMsi, mainInstaller, StringComparison.OrdinalIgnoreCase))
                    continue;

                var prereqLogPath = Path.Combine(
                    msiLogDir,
                    $"KlevaDeploy_{Guid.NewGuid():N}_Prereq_{Path.GetFileNameWithoutExtension(prereqMsi)}.msi.log");

                var prereqArgs = BuildGenericPrereqMsiArgs(prereqMsi, prereqLogPath);
                var prereqResult = new ProcessResult(1618, string.Empty, string.Empty);
                for (var attempt = 0; attempt < 25; attempt++)
                {
                    prereqResult = await _processExecutionService.RunAsync("msiexec.exe", prereqArgs, runAsAdmin: false, ct);
                    if (prereqResult.ExitCode != 1618)
                        break;
                    await Task.Delay(1000, ct);
                }

                if (prereqResult.ExitCode != 0 && prereqResult.ExitCode != 3010 && prereqResult.ExitCode != 1641)
                {
                    return new ProcessResult(
                        prereqResult.ExitCode,
                        string.Empty,
                        $"MSI prerequisite failed: {Path.GetFileName(prereqMsi)}. Log: {prereqLogPath}");
                }
            }
        }

        step.SetStatus("▶️", "Installing...");
        step.ProgressValue = 80;

        if (mainExt == ".msi")
        {
            var msiArgs = string.IsNullOrWhiteSpace(installDir)
                ? expandedArguments
                : AppendMsiInstallDirIfMissing(expandedArguments, installDir);
            return await RunMsiWithAppProgressAsync(step, mainInstaller, msiArgs, process.RunAsAdmin, ct);
        }

        var finalArgs = expandedArguments;
        if (finalArgs.Contains("{AUTO}", StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveSilentArgsForExeInstallerKnown(mainInstaller, out var silent))
                finalArgs = finalArgs.Replace("{AUTO}", silent, StringComparison.OrdinalIgnoreCase).Trim();
            else
            {
                finalArgs = finalArgs.Replace("{AUTO}", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
                _log.Warning($"Auto mode: extracted EXE installer family not recognized, running without silent args: {Path.GetFileName(mainInstaller)}");
            }
        }
        if (finalArgs.Contains("{SILENT}", StringComparison.OrdinalIgnoreCase))
        {
            var silent = ResolveSilentArgsForExeInstaller(mainInstaller);
            finalArgs = finalArgs.Replace("{SILENT}", silent, StringComparison.OrdinalIgnoreCase).Trim();
        }

        return await _processExecutionService.RunAsync(mainInstaller, finalArgs, process.RunAsAdmin, ct);
    }

    private static string BuildGenericPrereqMsiArgs(string msiPath, string logPath)
    {
        var args = $"/i \"{msiPath}\" /qn /norestart REBOOT=ReallySuppress ALLUSERS=2 /L*v \"{logPath}\"";
        var fileName = Path.GetFileName(msiPath) ?? string.Empty;
        if (fileName.Contains("sqlncli", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("nativeclient", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("native client", StringComparison.OrdinalIgnoreCase))
        {
            args = $"{args} IACCEPTSQLNCLILICENSETERMS=YES";
        }

        return args.Trim();
    }

    private static Dictionary<string, string> TryGetEnvVarDefaultsFromMsi(string msiPath, string rawArgs)
    {
        var map = InferEnvVarToMsiPropertyMap(rawArgs);
        if (map.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var props = map.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defaultsByProp = TryReadMsiPropertyTableValues(msiPath, props);
        if (defaultsByProp.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in map)
        {
            if (defaultsByProp.TryGetValue(kvp.Value, out var v) && v is not null)
                result[kvp.Key] = v;
        }
        return result;
    }

    private static Dictionary<string, string> InferEnvVarToMsiPropertyMap(string rawArgs)
    {
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

        static string TrimOuterQuotes(string s)
        {
            var v = (s ?? string.Empty).Trim();
            if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
                return v[1..^1];
            return v;
        }

        var text = rawArgs ?? string.Empty;
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                var t = sb.ToString().Trim();
                if (t.Length > 0) tokens.Add(t);
                sb.Clear();
                continue;
            }

            sb.Append(c);
        }
        {
            var t = sb.ToString().Trim();
            if (t.Length > 0) tokens.Add(t);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var idx = token.IndexOf('=');
            if (idx <= 0 || idx >= token.Length - 1) continue;

            var left = token[..idx].Trim();
            var right = token[(idx + 1)..].Trim();
            if (left.Length == 0 || right.Length == 0) continue;

            left = left.TrimStart('/', '-');
            if (!IsMsiPropertyName(left)) continue;

            right = TrimOuterQuotes(right);
            if (right.Length < 3) continue;
            if (right[0] != '%' || right[^1] != '%') continue;

            var env = right[1..^1].Trim();
            if (env.Length == 0) continue;
            if (!env.StartsWith("KLEVADEPLOY_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!IsMsiPropertyName(env)) continue;

            map[env] = left;
        }

        return map;
    }

    private static Dictionary<string, string> TryReadMsiPropertyTableValues(string msiPath, IReadOnlyList<string> propertyNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(msiPath) || !File.Exists(msiPath)) return result;
        if (propertyNames.Count == 0) return result;

        try
        {
            var installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
            if (installerType is null) return result;

            dynamic installer = Activator.CreateInstance(installerType)!;
            dynamic db = installer.OpenDatabase(msiPath, 0);
            var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dynamic view = db.OpenView("SELECT `Property`,`Value` FROM `Property`");
            view.Execute();
            while (true)
            {
                dynamic rec = view.Fetch();
                if (rec is null) break;
                try
                {
                    var k = (string)(rec.StringData[1] ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    var v = (string)(rec.StringData[2] ?? string.Empty);
                    all[k] = v ?? string.Empty;
                }
                catch { }
            }
            try { view.Close(); } catch { }

            foreach (var name in propertyNames)
            {
                var n = (name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (all.TryGetValue(n, out var v) && !string.IsNullOrWhiteSpace(v))
                {
                    result[n] = v ?? string.Empty;
                    continue;
                }

                var fromControl = TryReadMsiControlDefaultValue(installer, db, n);
                if (!string.IsNullOrWhiteSpace(fromControl))
                    result[n] = fromControl;
            }
        }
        catch
        {
            return result;
        }

        return result;
    }

    private static string? TryReadMsiControlDefaultValue(dynamic installer, dynamic db, string propertyName)
    {
        try
        {
            static bool LooksLikeDefaultValue(string? s)
            {
                var v = (s ?? string.Empty).Trim();
                if (v.Length == 0) return false;
                if (v.StartsWith("[", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }

            dynamic view = db.OpenView("SELECT `Type`,`Text` FROM `Control` WHERE `Property`=?");
            dynamic rec = installer.CreateRecord(1);
            rec.StringData[1] = propertyName;
            view.Execute(rec);

            string? best = null;
            while (true)
            {
                dynamic row = view.Fetch();
                if (row is null) break;
                try
                {
                    var type = (string)(row.StringData[1] ?? string.Empty);
                    var text = (string)(row.StringData[2] ?? string.Empty);

                    if (string.IsNullOrWhiteSpace(type) || !type.Contains("Edit", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!LooksLikeDefaultValue(text))
                        continue;

                    best = text.Trim();
                    break;
                }
                catch { }
            }

            try { view.Close(); } catch { }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        using var reader = new StringReader(text);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null) yield break;
            if (line.Length == 0) continue;
            yield return line;
        }
    }

    private static string ResolveExecutableResourcePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path is required.", nameof(relativePath));

        if (Path.IsPathRooted(relativePath))
            return relativePath;

        var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        if (string.IsNullOrWhiteSpace(storageDir))
            storageDir = Path.Combine(AppContext.BaseDirectory, "Data");

        var storagePath = Path.Combine(storageDir, relativePath);
        if (File.Exists(storagePath))
            return storagePath;

        var basePath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(basePath))
            return basePath;

        return storagePath;
    }

    private void UpdateArgumentProfileAfterRun(DeploymentProcess process, bool success)
    {
        var inputs = (process.ArgumentInputs ?? new List<ArgumentInputDefinition>())
            .Where(x => !string.IsNullOrWhiteSpace((x.Key ?? string.Empty).Trim()))
            .ToList();

        if (inputs.Count == 0)
            return;

        var processKey = GetArgumentProfileProcessKey(process);
        if (!_argumentPromptChoiceThisRun.TryGetValue(processKey, out var choice))
            return;

        if (choice != ArgumentPromptChoice.RunAlways)
            return;

        var prefs = _prefsService.Preferences;
        if (prefs.ProcessArgumentProfiles is null || prefs.ProcessArgumentProfiles.Count == 0)
            return;

        var schemaHash = ComputeArgumentInputsSchemaHash(inputs);
        var profile = prefs.ProcessArgumentProfiles
            .FirstOrDefault(p => string.Equals((p.ProcessId ?? string.Empty).Trim(), processKey, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            return;

        if (!string.Equals(profile.SchemaHash ?? string.Empty, schemaHash, StringComparison.OrdinalIgnoreCase))
            return;

        var newFailed = !success;
        if (profile.LastRunFailed == newFailed)
            return;

        profile.LastRunFailed = newFailed;
        _prefsService.Save();
    }

    private static string GetArgumentProfileProcessKey(DeploymentProcess process)
    {
        var id = (process.Id ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        var fingerprint = $"{process.Kind}|{(process.Name ?? string.Empty).Trim()}|{(process.RelativePath ?? string.Empty).Trim()}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
        return $"anon:{Convert.ToHexString(bytes)}";
    }

    private static string ComputeArgumentInputsSchemaHash(IReadOnlyList<ArgumentInputDefinition> inputs)
    {
        var canonical = new StringBuilder();
        foreach (var d in inputs
                     .Where(x => !string.IsNullOrWhiteSpace((x.Key ?? string.Empty).Trim()))
                     .OrderBy(x => (x.Key ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
        {
            canonical.Append((d.Key ?? string.Empty).Trim());
            canonical.Append('|');
            canonical.Append(d.IsRequired ? '1' : '0');
            canonical.Append('|');
            canonical.Append(d.IsSecret ? '1' : '0');
            canonical.Append('|');
            canonical.Append(d.Label ?? string.Empty);
            canonical.Append('|');
            canonical.Append(d.Description ?? string.Empty);
            canonical.Append('|');
            canonical.Append(d.DefaultValue ?? string.Empty);
            canonical.Append('\n');
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(bytes);
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed;

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string> values)
        {
            foreach (var kvp in values)
            {
                var key = (kvp.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(key)) continue;
                _original[key] = Environment.GetEnvironmentVariable(key);
                var raw = kvp.Value ?? string.Empty;
                string expanded;
                try
                {
                    expanded = Environment.ExpandEnvironmentVariables(raw);
                }
                catch
                {
                    expanded = raw;
                }
                Environment.SetEnvironmentVariable(key, expanded);
            }

            foreach (var kvp in BuildDerivedEnvironmentVariables(values))
            {
                _original.TryAdd(kvp.Key, Environment.GetEnvironmentVariable(kvp.Key));
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var kvp in _original)
                Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
        }
    }

    private sealed class CompositeScope : IDisposable
    {
        private readonly IDisposable? _a;
        private readonly IDisposable? _b;
        private bool _disposed;

        public CompositeScope(IDisposable? a, IDisposable? b)
        {
            _a = a;
            _b = b;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _b?.Dispose(); } catch { }
            try { _a?.Dispose(); } catch { }
        }
    }

    private static Dictionary<string, string> BuildDerivedEnvironmentVariables(IReadOnlyDictionary<string, string> values)
    {
        var derived = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in values)
        {
            var key = (kvp.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            string expanded;
            try
            {
                expanded = Environment.ExpandEnvironmentVariables(kvp.Value ?? string.Empty);
            }
            catch
            {
                expanded = kvp.Value ?? string.Empty;
            }

            var tcpEndpoint = TryResolveSqlServerTcpEndpoint(expanded);
            if (string.IsNullOrWhiteSpace(tcpEndpoint)) continue;
            derived[key + "_TCP"] = tcpEndpoint;
        }

        return derived;
    }

    private static string? TryResolveSqlServerTcpEndpoint(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (!trimmed.Contains('\\')) return null;
        if (trimmed.Contains(',')) return trimmed;

        var parts = trimmed.Split(new[] { '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;

        var host = parts[0].Trim();
        var instance = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(instance)) return null;

        var port = TryReadSqlInstanceTcpPort(instance);
        if (string.IsNullOrWhiteSpace(port)) return null;

        return $"{host},{port}";
    }

    private static string? TryReadSqlInstanceTcpPort(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return null;

        var normalized = instanceName.Trim();
        var candidates = new[]
        {
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL15.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL14.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL13.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL12.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL11.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll"
        };

        foreach (var subKey in candidates)
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            if (key is null) continue;

            var tcpPort = key.GetValue("TcpPort") as string;
            if (!string.IsNullOrWhiteSpace(tcpPort)) return tcpPort.Trim();

            var dynamicPort = key.GetValue("TcpDynamicPorts") as string;
            if (!string.IsNullOrWhiteSpace(dynamicPort)) return dynamicPort.Trim();
        }

        return null;
    }

    private string? TryBuildFriendlyMsiFailureHint(string logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            return null;

        var tail = ReadTextTail(logPath, maxChars: 120_000);
        if (string.IsNullOrWhiteSpace(tail))
            return null;

        var outOfDisk =
            tail.Contains("OutOfDiskSpace = 1", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("OutOfNoRbDiskSpace = 1", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("There is not enough space", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("not enough disk space", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("spazio su disco", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("spazio su disco insufficiente", StringComparison.OrdinalIgnoreCase);

        if (outOfDisk)
            return "Spazio su disco insufficiente per completare l'installazione.";

        var rebootPending =
            tail.Contains("MsiSystemRebootPending = 1", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("reboot pending", StringComparison.OrdinalIgnoreCase) ||
            tail.Contains("riavvio", StringComparison.OrdinalIgnoreCase) && tail.Contains("necess", StringComparison.OrdinalIgnoreCase);

        var hasSqlSetupError1001 = tail.Contains("Errore 1001", StringComparison.OrdinalIgnoreCase) ||
                                   tail.Contains("Error 1001", StringComparison.OrdinalIgnoreCase);

        if (hasSqlSetupError1001)
        {
            var mentionsSa =
                tail.Contains("utente sa", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("user sa", StringComparison.OrdinalIgnoreCase);

            var cannotCreateDbOrInsert =
                tail.Contains("non esiste", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("non può creare database", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("non puo creare database", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("effettuare inserimenti", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("cannot create database", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("cannot insert", StringComparison.OrdinalIgnoreCase);

            var mentionsDbServer =
                tail.Contains("Server database", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("IpServerDatabase", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("IPSERVERDATABASE", StringComparison.OrdinalIgnoreCase) ||
                tail.Contains("SQLPASS", StringComparison.OrdinalIgnoreCase);

            if (mentionsSa && cannotCreateDbOrInsert)
            {
                return rebootPending
                    ? "Errore SQL: credenziali/permessi non validi per l'utente sa (o sa disabilitato). Riavvia Windows (se richiesto), verifica che SQLPASS sia installato e avviato e che l'istanza sia in Mixed Mode con login sa abilitato. Controlla anche che password e server/istanza siano corretti (es: NOMEPC\\SQLPASS)."
                    : "Errore SQL: credenziali/permessi non validi per l'utente sa (o sa disabilitato). Verifica che SQLPASS sia installato e avviato e che l'istanza sia in Mixed Mode con login sa abilitato. Controlla anche che password e server/istanza siano corretti (es: NOMEPC\\SQLPASS).";
            }

            if (mentionsDbServer)
            {
                return rebootPending
                    ? "Connessione SQL non riuscita (Errore 1001). Riavvia Windows (se richiesto) e verifica che l'istanza SQLPASS sia attiva e raggiungibile."
                    : "Connessione SQL non riuscita (Errore 1001). Verifica che l'istanza SQLPASS sia attiva e raggiungibile (servizio MSSQL$SQLPASS avviato).";
            }

            return rebootPending
                ? "Errore SQL durante l'installazione (Errore 1001). Riavvia Windows (se richiesto) e riprova."
                : "Errore SQL durante l'installazione (Errore 1001). Verifica che SQLPASS sia installato e raggiungibile e che le credenziali siano corrette.";
        }

        if (rebootPending)
            return "È presente un riavvio di Windows in sospeso: riavvia e riprova.";

        return null;
    }

    private static string ReadTextTail(string path, int maxChars)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length <= 0) return string.Empty;
            var maxBytes = Math.Min((long)maxChars * 2, 256_000);
            var toRead = (int)Math.Min(fs.Length, maxBytes);
            fs.Seek(-toRead, SeekOrigin.End);
            var buf = new byte[toRead];
            var read = fs.Read(buf, 0, toRead);
            if (read <= 0) return string.Empty;

            try
            {
                return Encoding.UTF8.GetString(buf, 0, read);
            }
            catch
            {
                return Encoding.Default.GetString(buf, 0, read);
            }
        }
        catch
        {
            return string.Empty;
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
        ArgumentInputs = p.ArgumentInputs?.Select(x => new ArgumentInputDefinition
        {
            Key = x.Key,
            Label = x.Label,
            Description = x.Description,
            DefaultValue = x.DefaultValue,
            IsSecret = x.IsSecret,
            IsRequired = x.IsRequired
        }).ToList() ?? new(),
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

    private static DeploymentProcess MaterializeExternalScriptsForExport(DeploymentProcess process)
    {
        var clone = CloneProcess(process);

        if (ShouldInlineScriptContent(clone) &&
            TryReadExportableResourceText(clone.RelativePath, out var scriptText))
        {
            clone.ScriptContent = scriptText;
        }

        if (clone.SubProcesses is null || clone.SubProcesses.Count == 0)
            return clone;

        clone.SubProcesses = clone.SubProcesses
            .Select(sp => new DeploymentSubProcess
            {
                Name = sp.Name,
                RelativePath = sp.RelativePath,
                Arguments = sp.Arguments,
                RunAsAdmin = sp.RunAsAdmin,
                Process = sp.Process is null ? null : MaterializeExternalScriptsForExport(sp.Process)
            })
            .ToList();

        return clone;
    }

    private static bool ShouldInlineScriptContent(DeploymentProcess process) =>
        process.Kind is ProcessKind.PowerShellScript or ProcessKind.BatchScript or ProcessKind.BashScript &&
        string.IsNullOrWhiteSpace(process.ScriptContent) &&
        !string.IsNullOrWhiteSpace(process.RelativePath);

    private static bool TryReadExportableResourceText(string relativePath, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        if (string.IsNullOrWhiteSpace(storageDir))
            storageDir = Path.Combine(AppContext.BaseDirectory, "Data");

        var candidates = new[]
        {
            Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(storageDir, relativePath),
            Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(AppContext.BaseDirectory, relativePath)
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate)) continue;
            content = File.ReadAllText(candidate);
            return true;
        }

        return false;
    }

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

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            CreateNoWindow = true,
            UseShellExecute = runAsAdmin,
        };

        psi.ArgumentList.Add("--msi-worker");
        psi.ArgumentList.Add("--pipe");
        psi.ArgumentList.Add(pipeName);
        psi.ArgumentList.Add("--msi");
        psi.ArgumentList.Add(msiPath);
        psi.ArgumentList.Add("--msi-args");
        psi.ArgumentList.Add(msiArgs ?? string.Empty);

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
        string? workerLogPath = null;

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
                            {
                                workerLogPath = msg.LogPath;
                                _log.Info($"MSI log: {msg.LogPath}");
                            }
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
                            {
                                workerLogPath = msg.LogPath;
                                _log.Info($"MSI log: {msg.LogPath}");
                            }
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
        if (exitCode != 0 && exitCode != 3010 && exitCode != 1641 && !string.IsNullOrWhiteSpace(workerLogPath))
        {
            var friendly = TryBuildFriendlyMsiFailureHint(workerLogPath);
            var err = string.IsNullOrWhiteSpace(friendly)
                ? $"MSI install failed. Log: {workerLogPath}"
                : $"{friendly}\nLog: {workerLogPath}";
            return new ProcessResult(exitCode, string.Empty, err);
        }

        return new ProcessResult(exitCode, string.Empty, string.Empty);
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
        var vm = new SettingsViewModel(_appUpdateService, _prefsService, _themeService, _log, _dialogService, _presetIconService);
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
