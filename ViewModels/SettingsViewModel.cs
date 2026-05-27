using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using Microsoft.Win32;

namespace KlevaDeploy.ViewModels;

public enum SettingsSection
{
    InfoEAggiornamenti,
    Portali,
    Installer
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IAppUpdateService _appUpdateService;
    private readonly IPreferencesService _prefsService;
    private readonly ILogService _log;
    private readonly IDialogService _dialogService;
    private readonly IPresetIconService _presetIconService;

    public ObservableCollection<PortalEditorItemViewModel> Portals { get; } = new();
    public ObservableCollection<InstallerCacheItemViewModel> Installers { get; } = new();

    private SettingsSection _selectedSection = SettingsSection.InfoEAggiornamenti;
    public SettingsSection SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (value == _selectedSection) return;

            if (_selectedSection == SettingsSection.Portali && value != SettingsSection.Portali)
            {
                if (!TryLeavePortalEditor())
                {
                    OnPropertyChanged(nameof(SelectedSection));
                    return;
                }
            }

            SetProperty(ref _selectedSection, value);
        }
    }

    private bool _isCheckingForUpdate;
    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        set
        {
            if (!SetProperty(ref _isCheckingForUpdate, value)) return;
            CheckForUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isUpdateAvailable;
    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => SetProperty(ref _isUpdateAvailable, value);
    }

    private string _availableVersion = string.Empty;
    public string AvailableVersion
    {
        get => _availableVersion;
        set => SetProperty(ref _availableVersion, value);
    }

    private string _updateStatusText = string.Empty;
    public string UpdateStatusText
    {
        get => _updateStatusText;
        set => SetProperty(ref _updateStatusText, value);
    }

    public string AppVersion { get; }
    public string StorageDirectory { get; }
    public string InstallerCacheDirectory { get; }

    private string _installerStatusText = string.Empty;
    public string InstallerStatusText
    {
        get => _installerStatusText;
        set => SetProperty(ref _installerStatusText, value);
    }

    public IAsyncRelayCommand CheckForUpdateCommand { get; }
    public IRelayCommand SelectInfoEAggiornamentiCommand { get; }
    public IRelayCommand SelectPortaliCommand { get; }
    public IRelayCommand SelectInstallerCommand { get; }
    public IRelayCommand CloseCommand { get; }

    public IRelayCommand AddPortalCommand { get; }
    public IRelayCommand DeleteSelectedPortalCommand { get; }
    public IRelayCommand SaveSelectedPortalCommand { get; }
    public IRelayCommand OpenLogoPickerCommand { get; }
    public IRelayCommand CloseLogoPickerCommand { get; }
    public IRelayCommand ImportLogoCommand { get; }
    public IRelayCommand<PresetIconLibraryItem?> SelectLibraryLogoCommand { get; }
    public IRelayCommand RemoveLogoCommand { get; }
    public IRelayCommand SetLogoPickerTargetLightCommand { get; }
    public IRelayCommand SetLogoPickerTargetDarkCommand { get; }

    public IRelayCommand ApriCartellaInstallerCommand { get; }
    public IRelayCommand PulisciCacheInstallerCommand { get; }

    private PortalEditorItemViewModel? _selectedPortal;
    public PortalEditorItemViewModel? SelectedPortal
    {
        get => _selectedPortal;
        set
        {
            var previous = _selectedPortal;
            if (previous is not null && !ReferenceEquals(previous, value))
            {
                if (!TryLeavePortalEditor())
                {
                    OnPropertyChanged(nameof(SelectedPortal));
                    return;
                }
            }

            if (!SetProperty(ref _selectedPortal, value)) return;
            OnPropertyChanged(nameof(IsPortalEditorVisible));
            LoadPortalEditorFieldsFromSelection();
            DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
            SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsPortalEditorVisible => SelectedPortal is not null;

    private string _portalEditName = string.Empty;
    public string PortalEditName
    {
        get => _portalEditName;
        set
        {
            if (!SetProperty(ref _portalEditName, value)) return;
            OnPropertyChanged(nameof(HasUnsavedPortalChanges));
            SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        }
    }

    private string _portalEditHomeUrl = string.Empty;
    public string PortalEditHomeUrl
    {
        get => _portalEditHomeUrl;
        set
        {
            if (!SetProperty(ref _portalEditHomeUrl, value)) return;
            OnPropertyChanged(nameof(HasUnsavedPortalChanges));
            SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _portalEditLogoLightPath;
    public string? PortalEditLogoLightPath
    {
        get => _portalEditLogoLightPath;
        set
        {
            if (!SetProperty(ref _portalEditLogoLightPath, value)) return;
            OnPropertyChanged(nameof(HasPortalLogo));
            OnPropertyChanged(nameof(HasUnsavedPortalChanges));
            SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        }
    }

    private string? _portalEditLogoDarkPath;
    public string? PortalEditLogoDarkPath
    {
        get => _portalEditLogoDarkPath;
        set
        {
            if (!SetProperty(ref _portalEditLogoDarkPath, value)) return;
            OnPropertyChanged(nameof(HasPortalLogo));
            OnPropertyChanged(nameof(HasUnsavedPortalChanges));
            SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasPortalLogo => !string.IsNullOrWhiteSpace(PortalEditLogoLightPath) || !string.IsNullOrWhiteSpace(PortalEditLogoDarkPath);

    private bool _useSeparateThemeLogos;
    public bool UseSeparateThemeLogos
    {
        get => _useSeparateThemeLogos;
        set
        {
            if (!SetProperty(ref _useSeparateThemeLogos, value)) return;
            if (!value)
            {
                if (!string.IsNullOrWhiteSpace(PortalEditLogoLightPath))
                {
                    PortalEditLogoDarkPath = PortalEditLogoLightPath;
                }
                else if (!string.IsNullOrWhiteSpace(PortalEditLogoDarkPath))
                {
                    PortalEditLogoLightPath = PortalEditLogoDarkPath;
                }
            }
            OnPropertyChanged(nameof(HasUnsavedPortalChanges));
        }
    }

    private bool _isLogoPickerOpen;
    public bool IsLogoPickerOpen
    {
        get => _isLogoPickerOpen;
        set => SetProperty(ref _isLogoPickerOpen, value);
    }

    private bool _isLogoPickerTargetDark;
    public bool IsLogoPickerTargetDark
    {
        get => _isLogoPickerTargetDark;
        set => SetProperty(ref _isLogoPickerTargetDark, value);
    }

    public ObservableCollection<PresetIconLibraryItem> LogoLibraryIcons { get; } = new();

    public bool HasUnsavedPortalChanges
    {
        get
        {
            if (SelectedPortal is null) return false;
            if (SelectedPortal.IsDraft) return true;

            var nameChanged = !string.Equals((PortalEditName ?? string.Empty).Trim(), (SelectedPortal.Name ?? string.Empty).Trim(), StringComparison.Ordinal);
            var urlChanged = !string.Equals(
                NormalizePortalHomeUrl(PortalEditHomeUrl),
                NormalizePortalHomeUrl(SelectedPortal.HomeUrl),
                StringComparison.OrdinalIgnoreCase);
            var logoLightChanged = !string.Equals((PortalEditLogoLightPath ?? string.Empty).Trim(), (SelectedPortal.LogoLightPath ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
            var logoDarkChanged = !string.Equals((PortalEditLogoDarkPath ?? string.Empty).Trim(), (SelectedPortal.LogoDarkPath ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
            return nameChanged || urlChanged || logoLightChanged || logoDarkChanged;
        }
    }

    public event EventHandler? CloseRequested;

    public SettingsViewModel(IAppUpdateService appUpdateService, IPreferencesService prefsService, ILogService log, IDialogService dialogService, IPresetIconService presetIconService)
    {
        _appUpdateService = appUpdateService;
        _prefsService = prefsService;
        _log = log;
        _dialogService = dialogService;
        _presetIconService = presetIconService;

        AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        StorageDirectory = ResolveStorageDirectory();
        InstallerCacheDirectory = ResolveInstallerCacheRootDirectory();

        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync, CanCheckForUpdate);
        SelectInfoEAggiornamentiCommand = new RelayCommand(() => SelectedSection = SettingsSection.InfoEAggiornamenti);
        SelectPortaliCommand = new RelayCommand(() => SelectedSection = SettingsSection.Portali);
        SelectInstallerCommand = new RelayCommand(() => SelectedSection = SettingsSection.Installer);
        CloseCommand = new RelayCommand(Close);

        AddPortalCommand = new RelayCommand(AddPortal);
        DeleteSelectedPortalCommand = new RelayCommand(DeleteSelectedPortal, CanDeleteSelectedPortal);
        SaveSelectedPortalCommand = new RelayCommand(SaveSelectedPortal, CanSaveSelectedPortal);
        OpenLogoPickerCommand = new RelayCommand(OpenLogoPicker);
        CloseLogoPickerCommand = new RelayCommand(() => IsLogoPickerOpen = false);
        ImportLogoCommand = new RelayCommand(ImportLogo);
        SelectLibraryLogoCommand = new RelayCommand<PresetIconLibraryItem?>(SelectLibraryLogo);
        RemoveLogoCommand = new RelayCommand(RemoveLogo);
        SetLogoPickerTargetLightCommand = new RelayCommand(() => IsLogoPickerTargetDark = false);
        SetLogoPickerTargetDarkCommand = new RelayCommand(() => IsLogoPickerTargetDark = true);

        ApriCartellaInstallerCommand = new RelayCommand(ApriCartellaInstaller);
        PulisciCacheInstallerCommand = new RelayCommand(PulisciCacheInstaller);

        LoadPortalsFromPreferences();
    }

    public void LoadInstallers(IReadOnlyList<DeploymentProcess> allProcesses)
    {
        Installers.Clear();
        foreach (var p in allProcesses.Where(p => p.Kind == ProcessKind.Installer))
        {
            Installers.Add(new InstallerCacheItemViewModel(p, ResolveInstallerCacheDirectory(p), ResolveInstallerCacheFileName(p)));
        }
    }

    private async Task CheckForUpdateAsync()
    {
        IsCheckingForUpdate = true;
        UpdateStatusText = string.Empty;
        try
        {
            var info = await _appUpdateService.CheckForUpdateAsync();
            if (info is null)
            {
                IsUpdateAvailable = false;
                AvailableVersion = string.Empty;
                UpdateStatusText = "Nessun aggiornamento disponibile.";
            }
            else
            {
                IsUpdateAvailable = true;
                AvailableVersion = info.Version;
                UpdateStatusText = $"Aggiornamento disponibile: {info.Version}";
            }
        }
        catch (Exception ex)
        {
            _log.Error("Settings update check failed", ex);
            IsUpdateAvailable = false;
            AvailableVersion = string.Empty;
            UpdateStatusText = "Errore durante il controllo aggiornamenti.";
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    private bool CanCheckForUpdate() => !IsCheckingForUpdate;

    private void ApriCartellaInstaller()
    {
        try
        {
            Directory.CreateDirectory(InstallerCacheDirectory);
            Process.Start(new ProcessStartInfo(InstallerCacheDirectory) { UseShellExecute = true });
            InstallerStatusText = string.Empty;
        }
        catch (Exception ex)
        {
            _log.Error("Open installers folder failed", ex);
            InstallerStatusText = "Impossibile aprire la cartella degli installer.";
        }
    }

    private void PulisciCacheInstaller()
    {
        try
        {
            if (!Directory.Exists(InstallerCacheDirectory))
            {
                InstallerStatusText = "La cache è già vuota.";
                return;
            }

            foreach (var dir in Directory.EnumerateDirectories(InstallerCacheDirectory))
                Directory.Delete(dir, true);
            foreach (var file in Directory.EnumerateFiles(InstallerCacheDirectory))
                File.Delete(file);

            InstallerStatusText = "Cache installer pulita.";
        }
        catch (Exception ex)
        {
            _log.Error("Clear installers cache failed", ex);
            InstallerStatusText = "Errore durante la pulizia della cache.";
        }
    }

    private void LoadPortalsFromPreferences()
    {
        var prefs = _prefsService.Preferences;
        prefs.Portals ??= new();
        if (prefs.Portals.Count == 0)
        {
            prefs.Portals.Add(new PortalPreference
            {
                Id = "passepartout",
                Name = "Passepartout",
                HomeUrl = "https://download.passepartout.cloud/."
            });
        }

        Portals.Clear();
        foreach (var p in prefs.Portals.Where(p => p is not null))
        {
            Portals.Add(new PortalEditorItemViewModel
            {
                Id = p.Id,
                Name = p.Name,
                HomeUrl = p.HomeUrl,
                LogoLightPath = p.LogoLightPath,
                LogoDarkPath = p.LogoDarkPath,
                IsDraft = false
            });
        }

        var selected = Portals.FirstOrDefault(p => string.Equals(p.Id, prefs.SelectedPortalId, StringComparison.OrdinalIgnoreCase))
                       ?? Portals.FirstOrDefault();
        SelectedPortal = selected;

        DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
        SaveSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    private void PersistPortalsToPreferences()
    {
        var prefs = _prefsService.Preferences;
        prefs.Portals = Portals
            .Where(p => !p.IsDraft)
            .Select(p => new PortalPreference
            {
                Id = p.Id,
                Name = string.IsNullOrWhiteSpace(p.Name) ? "Portale" : p.Name.Trim(),
                HomeUrl = NormalizePortalHomeUrl(p.HomeUrl),
                LastUsername = prefs.Portals.FirstOrDefault(x => string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))?.LastUsername,
                LogoLightPath = string.IsNullOrWhiteSpace(p.LogoLightPath) ? null : p.LogoLightPath.Trim(),
                LogoDarkPath = string.IsNullOrWhiteSpace(p.LogoDarkPath) ? null : p.LogoDarkPath.Trim()
            })
            .ToList();

        if (string.IsNullOrWhiteSpace(prefs.SelectedPortalId))
            prefs.SelectedPortalId = prefs.Portals.FirstOrDefault()?.Id;

        var selectedPortal = prefs.Portals.FirstOrDefault(x => string.Equals(x.Id, prefs.SelectedPortalId, StringComparison.OrdinalIgnoreCase));
        if (selectedPortal is not null)
            prefs.SelectedPortalHomeUrl = selectedPortal.HomeUrl;

        _prefsService.Save();
    }

    private void AddPortal()
    {
        var portal = new PortalEditorItemViewModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Nuovo portale",
            HomeUrl = "https://",
            LogoLightPath = null,
            LogoDarkPath = null,
            IsDraft = true
        };
        Portals.Insert(0, portal);
        SelectedPortal = portal;
        SelectedSection = SettingsSection.Portali;

        DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
        SaveSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    private bool CanDeleteSelectedPortal()
    {
        if (SelectedPortal is null) return false;
        if (Portals.Count <= 1) return false;
        return true;
    }

    private void DeleteSelectedPortal()
    {
        if (SelectedPortal is null) return;
        if (Portals.Count <= 1) return;

        var toRemove = SelectedPortal;
        var portalName = string.IsNullOrWhiteSpace(toRemove.Name) ? "Portale" : toRemove.Name.Trim();
        if (!_dialogService.Confirm("Conferma eliminazione", $"Sei sicuro di voler eliminare il collegamento al portal \"{portalName}\"?"))
            return;

        if (toRemove.IsDraft)
        {
            Portals.Remove(toRemove);
            SelectedPortal = Portals.FirstOrDefault();
            DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
            SaveSelectedPortalCommand.NotifyCanExecuteChanged();
            return;
        }

        var idx = Portals.IndexOf(toRemove);
        Portals.Remove(toRemove);

        SelectedPortal = Portals.Count == 0
            ? null
            : Portals[Math.Clamp(idx - 1, 0, Portals.Count - 1)];

        var prefs = _prefsService.Preferences;
        if (string.Equals(prefs.SelectedPortalId, toRemove.Id, StringComparison.OrdinalIgnoreCase))
            prefs.SelectedPortalId = SelectedPortal?.Id;

        PersistPortalsToPreferences();

        DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
        SaveSelectedPortalCommand.NotifyCanExecuteChanged();
    }

    private bool CanSaveSelectedPortal()
    {
        if (SelectedPortal is null) return false;
        if (string.IsNullOrWhiteSpace(PortalEditName)) return false;
        if (string.IsNullOrWhiteSpace(PortalEditHomeUrl)) return false;
        if (!Uri.TryCreate(NormalizePortalHomeUrl(PortalEditHomeUrl), UriKind.Absolute, out _)) return false;
        return HasUnsavedPortalChanges;
    }

    private void SaveSelectedPortal()
    {
        if (SelectedPortal is null) return;
        SelectedPortal.Name = PortalEditName.Trim();
        SelectedPortal.HomeUrl = NormalizePortalHomeUrl(PortalEditHomeUrl);
        SelectedPortal.LogoLightPath = string.IsNullOrWhiteSpace(PortalEditLogoLightPath) ? null : PortalEditLogoLightPath.Trim();
        SelectedPortal.LogoDarkPath = string.IsNullOrWhiteSpace(PortalEditLogoDarkPath) ? null : PortalEditLogoDarkPath.Trim();
        SelectedPortal.IsDraft = false;
        PersistPortalsToPreferences();

        DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
        SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasUnsavedPortalChanges));
    }

    private void Close()
    {
        if (!TryLeavePortalEditor())
            return;

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public bool CanClose() => TryLeavePortalEditor();

    private bool TryLeavePortalEditor()
    {
        if (SelectedPortal is null) return true;
        if (!HasUnsavedPortalChanges) return true;

        var portalName = string.IsNullOrWhiteSpace(SelectedPortal.Name) ? "Portal" : SelectedPortal.Name.Trim();
        if (!_dialogService.Confirm("Unsaved changes", $"You have unsaved changes for portal \"{portalName}\". Discard them?"))
            return false;

        if (SelectedPortal.IsDraft)
        {
            var toRemove = SelectedPortal;
            Portals.Remove(toRemove);
            _selectedPortal = null;
            OnPropertyChanged(nameof(SelectedPortal));
            OnPropertyChanged(nameof(IsPortalEditorVisible));
        }

        LoadPortalEditorFieldsFromSelection();
        DeleteSelectedPortalCommand.NotifyCanExecuteChanged();
        SaveSelectedPortalCommand.NotifyCanExecuteChanged();
        return true;
    }

    private void OpenLogoPicker()
    {
        LoadLogoLibraryIcons();
        IsLogoPickerOpen = true;
    }

    private void LoadLogoLibraryIcons()
    {
        LogoLibraryIcons.Clear();
        foreach (var item in _presetIconService.GetLibraryIcons())
        {
            LogoLibraryIcons.Add(item);
        }
    }

    private void ImportLogo()
    {
        var path = PickLogoFile("Seleziona logo");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var item = _presetIconService.ImportLibraryIcon(path);
            LogoLibraryIcons.Insert(0, item);
            SelectLibraryLogo(item);
        }
        catch (Exception ex)
        {
            _log.Error("Logo import failed", ex);
        }
    }

    private void SelectLibraryLogo(PresetIconLibraryItem? item)
    {
        if (item is null) return;
        if (string.IsNullOrWhiteSpace(item.LightPath) && string.IsNullOrWhiteSpace(item.DarkPath)) return;

        if (!UseSeparateThemeLogos)
        {
            var candidate = !string.IsNullOrWhiteSpace(item.LightPath) ? item.LightPath : item.DarkPath;
            PortalEditLogoLightPath = candidate;
            PortalEditLogoDarkPath = !string.IsNullOrWhiteSpace(item.DarkPath) ? item.DarkPath : candidate;
            IsLogoPickerOpen = false;
            return;
        }

        if (IsLogoPickerTargetDark)
        {
            PortalEditLogoDarkPath = !string.IsNullOrWhiteSpace(item.DarkPath) ? item.DarkPath : item.LightPath;
        }
        else
        {
            PortalEditLogoLightPath = !string.IsNullOrWhiteSpace(item.LightPath) ? item.LightPath : item.DarkPath;
        }
    }

    private void RemoveLogo()
    {
        PortalEditLogoLightPath = null;
        PortalEditLogoDarkPath = null;
        UseSeparateThemeLogos = false;
    }

    private static string? PickLogoFile(string title)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "Images (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico",
            Multiselect = false
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void LoadPortalEditorFieldsFromSelection()
    {
        if (SelectedPortal is null)
        {
            PortalEditName = string.Empty;
            PortalEditHomeUrl = string.Empty;
            PortalEditLogoLightPath = null;
            PortalEditLogoDarkPath = null;
            UseSeparateThemeLogos = false;
            return;
        }

        PortalEditName = SelectedPortal.Name ?? string.Empty;
        PortalEditHomeUrl = SelectedPortal.HomeUrl ?? string.Empty;
        PortalEditLogoLightPath = SelectedPortal.LogoLightPath;
        PortalEditLogoDarkPath = SelectedPortal.LogoDarkPath;
        UseSeparateThemeLogos = !string.IsNullOrWhiteSpace(PortalEditLogoLightPath) &&
                                !string.IsNullOrWhiteSpace(PortalEditLogoDarkPath) &&
                                !string.Equals(PortalEditLogoLightPath.Trim(), PortalEditLogoDarkPath.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePortalHomeUrl(string? portalHomeUrl)
    {
        var raw = (portalHomeUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return "https://download.passepartout.cloud/.";
        if (!raw.Contains("://", StringComparison.OrdinalIgnoreCase))
            raw = "https://" + raw.TrimStart('/');
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

    private string ResolveStorageDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    private static string ResolveInstallerCacheRootDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Data", "installers");

    private static string ResolveInstallerCacheDirectory(DeploymentProcess process) =>
        Path.Combine(AppContext.BaseDirectory, "Data", "installers", process.Id);

    private static string ResolveInstallerCacheFileName(DeploymentProcess process)
    {
        if (process.InstallerSourceMode == InstallerSourceMode.StaticWeb && !string.IsNullOrWhiteSpace(process.DownloadUrl))
        {
            if (Uri.TryCreate(process.DownloadUrl.Trim(), UriKind.Absolute, out var uri))
                return Path.GetFileName(uri.LocalPath);
        }

        if (!string.IsNullOrWhiteSpace(process.DownloadSelectedFileName))
            return process.DownloadSelectedFileName;

        return string.Empty;
    }
}

public sealed class PortalEditorItemViewModel : ObservableObject
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

    private string? _logoLightPath;
    public string? LogoLightPath
    {
        get => _logoLightPath;
        set => SetProperty(ref _logoLightPath, value);
    }

    private string? _logoDarkPath;
    public string? LogoDarkPath
    {
        get => _logoDarkPath;
        set => SetProperty(ref _logoDarkPath, value);
    }

    private bool _isDraft;
    public bool IsDraft
    {
        get => _isDraft;
        set => SetProperty(ref _isDraft, value);
    }
}

public sealed class InstallerCacheItemViewModel : ObservableObject
{
    public DeploymentProcess Process { get; }

    public string Name => Process.Name;
    public string SourceMode => Process.InstallerSourceMode.ToString();
    public string CacheDirectory { get; }
    public string CacheFileName { get; }

    public bool HasCachedFile => !string.IsNullOrWhiteSpace(CachedFilePath) && File.Exists(CachedFilePath);
    public string CachedFilePath => string.IsNullOrWhiteSpace(CacheFileName) ? string.Empty : Path.Combine(CacheDirectory, CacheFileName);

    public IRelayCommand OpenCacheFolderCommand { get; }
    public IRelayCommand ClearCacheCommand { get; }

    public InstallerCacheItemViewModel(DeploymentProcess process, string cacheDirectory, string cacheFileName)
    {
        Process = process;
        CacheDirectory = cacheDirectory;
        CacheFileName = cacheFileName;

        OpenCacheFolderCommand = new RelayCommand(OpenCacheFolder);
        ClearCacheCommand = new RelayCommand(ClearCache, CanClearCache);
    }

    private bool CanClearCache() => Directory.Exists(CacheDirectory);

    private void OpenCacheFolder()
    {
        Directory.CreateDirectory(CacheDirectory);
        System.Diagnostics.Process.Start(new ProcessStartInfo(CacheDirectory) { UseShellExecute = true });
    }

    private void ClearCache()
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;

            foreach (var dir in Directory.EnumerateDirectories(CacheDirectory))
                Directory.Delete(dir, true);
            foreach (var file in Directory.EnumerateFiles(CacheDirectory))
                File.Delete(file);
        }
        finally
        {
            OnPropertyChanged(nameof(HasCachedFile));
            (ClearCacheCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }
    }
}
