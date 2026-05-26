using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using Microsoft.Win32;

namespace KlevaDeploy.ViewModels;

public sealed class CreateProcessViewModel : ObservableObject
{
    private static readonly Regex VersionFolderRegex = new(@"^(?<year>\d{4})(?<suffix>[A-Za-z0-9]+)$", RegexOptions.Compiled);
    private readonly IAuthService? _authService;
    private readonly IDownloadDirectoryListingService? _downloadDirectoryListingService;
    private readonly IPresetIconService? _presetIconService;
    private readonly ILogService? _log;
    private string _latestRemoteFolderName = string.Empty;
    private CancellationTokenSource? _remoteInstallerLoadCts;
    private bool _suppressVersionAutoLoad;

    private string? _existingProcessId;
    public string? EditingProcessId => _existingProcessId;

    private string _processName = string.Empty;
    public string ProcessName
    {
        get => _processName;
        set => SetProperty(ref _processName, value);
    }

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    private ProcessKind _selectedProcessKind = ProcessKind.Installer;
    public ProcessKind SelectedProcessKind
    {
        get => _selectedProcessKind;
        set
        {
            if (!SetProperty(ref _selectedProcessKind, value)) return;
            OnPropertyChanged(nameof(IsInstallerMode));
            OnPropertyChanged(nameof(IsScriptMode));
            OnPropertyChanged(nameof(IsRequiresInternetLocked));
            OnPropertyChanged(nameof(IsRequiresInternetUserEditable));
            ValidationError = null;
        }
    }

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set => SetProperty(ref _filePath, value);
    }

    private string _arguments = string.Empty;
    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    private string _scriptContent = string.Empty;
    public string ScriptContent
    {
        get => _scriptContent;
        set => SetProperty(ref _scriptContent, value);
    }

    private bool _runAsAdmin;
    public bool RunAsAdmin
    {
        get => _runAsAdmin;
        set => SetProperty(ref _runAsAdmin, value);
    }

    private bool _requiresInternet;
    public bool RequiresInternet
    {
        get => _requiresInternet;
        set => SetProperty(ref _requiresInternet, value);
    }

    private string _downloadBaseFolderUrl = string.Empty;
    public string DownloadBaseFolderUrl
    {
        get => _downloadBaseFolderUrl;
        set
        {
            if (!SetProperty(ref _downloadBaseFolderUrl, value)) return;
            RefreshRemoteInstallerFilesCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanNavigateUpRemoteFolder));
            OpenSelectedRemoteFolderCommand.NotifyCanExecuteChanged();
            NavigateUpRemoteFolderCommand.NotifyCanExecuteChanged();
        }
    }

    private string _downloadUrl = string.Empty;
    public string DownloadUrl
    {
        get => _downloadUrl;
        set => SetProperty(ref _downloadUrl, value);
    }

    private string _downloadSelectedFileName = string.Empty;
    public string DownloadSelectedFileName
    {
        get => _downloadSelectedFileName;
        set => SetProperty(ref _downloadSelectedFileName, value);
    }

    public ObservableCollection<string> RemoteInstallerFiles { get; } = new();

    public ObservableCollection<string> RemoteInstallerVersions { get; } = new();

    public bool HasRemoteInstallerVersions => RemoteInstallerVersions.Count > 0;

    private string _downloadSelectedVersion = string.Empty;
    public string DownloadSelectedVersion
    {
        get => _downloadSelectedVersion;
        set
        {
            if (!SetProperty(ref _downloadSelectedVersion, value)) return;
            if (!_suppressVersionAutoLoad)
                _ = OnDownloadSelectedVersionChangedAsync();
            OpenSelectedRemoteFolderCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isRemoteFolderNavigationMode;
    public bool IsRemoteFolderNavigationMode
    {
        get => _isRemoteFolderNavigationMode;
        set
        {
            if (!SetProperty(ref _isRemoteFolderNavigationMode, value)) return;
            OnPropertyChanged(nameof(RemoteFolderPickerLabel));
            OpenSelectedRemoteFolderCommand.NotifyCanExecuteChanged();
            NavigateUpRemoteFolderCommand.NotifyCanExecuteChanged();
        }
    }

    public string RemoteFolderPickerLabel => IsRemoteFolderNavigationMode ? "Sottocartella" : "Versione";

    public bool CanNavigateUpRemoteFolder => TryGetParentFolderUrl(DownloadBaseFolderUrl) is not null;

    private InstallerSourceMode _installerSourceMode = InstallerSourceMode.StaticLocal;
    public InstallerSourceMode InstallerSourceMode
    {
        get => _installerSourceMode;
        set
        {
            if (!SetProperty(ref _installerSourceMode, value)) return;
            OnPropertyChanged(nameof(IsInstallerSourceLocal));
            OnPropertyChanged(nameof(IsInstallerSourceStaticWeb));
            OnPropertyChanged(nameof(IsInstallerSourceDynamicWeb));
            OnPropertyChanged(nameof(IsRequiresInternetLocked));
            OnPropertyChanged(nameof(IsRequiresInternetUserEditable));
            RefreshRemoteInstallerFilesCommand.NotifyCanExecuteChanged();
            ValidationError = null;

            if (IsInstallerMode && _installerSourceMode != InstallerSourceMode.StaticLocal)
            {
                RequiresInternet = true;
            }
            else if (IsInstallerMode && _installerSourceMode == InstallerSourceMode.StaticLocal)
            {
                RequiresInternet = false;
            }
        }
    }

    public bool IsInstallerSourceLocal
    {
        get => InstallerSourceMode == InstallerSourceMode.StaticLocal;
        set
        {
            if (!value) return;
            InstallerSourceMode = InstallerSourceMode.StaticLocal;
        }
    }

    public bool IsInstallerSourceStaticWeb
    {
        get => InstallerSourceMode == InstallerSourceMode.StaticWeb;
        set
        {
            if (!value) return;
            InstallerSourceMode = InstallerSourceMode.StaticWeb;
        }
    }

    public bool IsInstallerSourceDynamicWeb
    {
        get => InstallerSourceMode == InstallerSourceMode.DynamicWeb;
        set
        {
            if (!value) return;
            InstallerSourceMode = InstallerSourceMode.DynamicWeb;
        }
    }

    private string _selectedIconKey = "IconPackage";
    public string SelectedIconKey
    {
        get => _selectedIconKey;
        set => SetProperty(ref _selectedIconKey, value);
    }

    private string _icon = "📦";
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    private string? _customIconLightPath;
    public string? CustomIconLightPath
    {
        get => _customIconLightPath;
        set
        {
            if (!SetProperty(ref _customIconLightPath, value)) return;
            OnPropertyChanged(nameof(HasCustomIcon));
        }
    }

    private string? _customIconDarkPath;
    public string? CustomIconDarkPath
    {
        get => _customIconDarkPath;
        set
        {
            if (!SetProperty(ref _customIconDarkPath, value)) return;
            OnPropertyChanged(nameof(HasCustomIcon));
        }
    }

    private bool _isIconPickerOpen;
    public bool IsIconPickerOpen
    {
        get => _isIconPickerOpen;
        set => SetProperty(ref _isIconPickerOpen, value);
    }

    private bool _useSeparateThemeIcons;
    public bool UseSeparateThemeIcons
    {
        get => _useSeparateThemeIcons;
        set => SetProperty(ref _useSeparateThemeIcons, value);
    }

    private bool _isIconPickerTargetDark;
    public bool IsIconPickerTargetDark
    {
        get => _isIconPickerTargetDark;
        set => SetProperty(ref _isIconPickerTargetDark, value);
    }

    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(CustomIconLightPath) || !string.IsNullOrWhiteSpace(CustomIconDarkPath);

    private string? _validationError;
    public string? ValidationError
    {
        get => _validationError;
        set => SetProperty(ref _validationError, value);
    }

    private string _title = "Nuovo Processo";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private bool _isEditMode;
    public bool IsEditMode
    {
        get => _isEditMode;
        set => SetProperty(ref _isEditMode, value);
    }

    private bool _canDelete;
    public bool CanDelete
    {
        get => _canDelete;
        set => SetProperty(ref _canDelete, value);
    }

    public ObservableCollection<ProcessKind> AvailableProcessKinds { get; } = new()
    {
        ProcessKind.Installer,
        ProcessKind.PowerShellScript,
        ProcessKind.BatchScript,
        ProcessKind.BashScript
    };

    public ObservableCollection<IconOption> AvailableIcons { get; } = new()
    {
        new("IconPackage", "📦 Pacchetto"),
        new("IconScript", "💻 Script"),
        new("IconSettings", "⚙️ Impostazioni"),
        new("IconDownload", "⬇️ Download"),
        new("IconInstall", "▶️ Installa"),
        new("IconSuccess", "✅ Successo"),
        new("IconWarning", "⚠️ Avviso"),
        new("IconError", "❌ Errore"),
        new("IconRefresh", "🔄 Aggiorna"),
        new("IconLock", "🔒 Blocca"),
        new("IconUnlock", "🔓 Sblocca"),
        new("IconLog", "📋 Log")
    };

    public ObservableCollection<string> AvailableEmojiIcons { get; } = new()
    {
        "📦", "🖥️", "📊", "💻", "🏢", "🖧", "⚙️", "🔧",
        "📁", "📂", "🗂️", "💾", "🔒", "🔓", "✅", "⚠️"
    };

    public ObservableCollection<PresetIconLibraryItem> LibraryIcons { get; } = new();

    public bool IsInstallerMode => SelectedProcessKind == ProcessKind.Installer;
    public bool IsScriptMode =>
        SelectedProcessKind == ProcessKind.PowerShellScript ||
        SelectedProcessKind == ProcessKind.BatchScript ||
        SelectedProcessKind == ProcessKind.BashScript;

    public bool IsRequiresInternetLocked => IsInstallerMode && InstallerSourceMode != InstallerSourceMode.StaticLocal;
    public bool IsRequiresInternetUserEditable => !IsRequiresInternetLocked;

    public DeploymentProcess? CreatedProcess { get; private set; }
    private bool? _dialogResult;
    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public IRelayCommand BrowseFileCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IAsyncRelayCommand RefreshRemoteInstallerFilesCommand { get; }
    public IAsyncRelayCommand OpenSelectedRemoteFolderCommand { get; }
    public IAsyncRelayCommand NavigateUpRemoteFolderCommand { get; }
    public IRelayCommand OpenIconPickerCommand { get; }
    public IRelayCommand CloseIconPickerCommand { get; }
    public IRelayCommand<string?> SelectEmojiIconCommand { get; }
    public IRelayCommand ImportLibraryIconCommand { get; }
    public IRelayCommand<PresetIconLibraryItem?> SelectLibraryIconCommand { get; }
    public IRelayCommand SetIconPickerTargetLightCommand { get; }
    public IRelayCommand SetIconPickerTargetDarkCommand { get; }
    public IRelayCommand RemoveCustomIconCommand { get; }

    public event EventHandler? DeleteRequested;

    public CreateProcessViewModel(IAuthService? authService = null, IDownloadDirectoryListingService? downloadDirectoryListingService = null, ILogService? log = null, IPresetIconService? presetIconService = null)
    {
        _authService = authService;
        _downloadDirectoryListingService = downloadDirectoryListingService;
        _log = log;
        _presetIconService = presetIconService;

        RemoteInstallerVersions.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(HasRemoteInstallerVersions));
        };

        BrowseFileCommand = new RelayCommand(BrowseFile);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(RequestDelete);
        RefreshRemoteInstallerFilesCommand = new AsyncRelayCommand(RefreshRemoteInstallerFilesAsync, CanRefreshRemoteInstallerFiles);
        OpenSelectedRemoteFolderCommand = new AsyncRelayCommand(OpenSelectedRemoteFolderAsync, CanOpenSelectedRemoteFolder);
        NavigateUpRemoteFolderCommand = new AsyncRelayCommand(NavigateUpRemoteFolderAsync, CanNavigateUpRemoteFolderAsync);
        OpenIconPickerCommand = new RelayCommand(OpenIconPicker);
        CloseIconPickerCommand = new RelayCommand(CloseIconPicker);
        SelectEmojiIconCommand = new RelayCommand<string?>(SelectEmojiIcon);
        ImportLibraryIconCommand = new RelayCommand(ImportLibraryIcon);
        SelectLibraryIconCommand = new RelayCommand<PresetIconLibraryItem?>(SelectLibraryIcon);
        SetIconPickerTargetLightCommand = new RelayCommand(SetIconPickerTargetLight);
        SetIconPickerTargetDarkCommand = new RelayCommand(SetIconPickerTargetDark);
        RemoveCustomIconCommand = new RelayCommand(RemoveCustomIcon);
    }

    public void InitializeNew()
    {
        _existingProcessId = null;
        ProcessName = string.Empty;
        Description = string.Empty;
        SelectedProcessKind = ProcessKind.Installer;
        InstallerSourceMode = InstallerSourceMode.StaticLocal;
        FilePath = string.Empty;
        Arguments = string.Empty;
        ScriptContent = string.Empty;
        RunAsAdmin = false;
        RequiresInternet = false;
        DownloadBaseFolderUrl = string.Empty;
        DownloadUrl = string.Empty;
        DownloadSelectedFileName = string.Empty;
        DownloadSelectedVersion = string.Empty;
        RemoteInstallerFiles.Clear();
        RemoteInstallerVersions.Clear();
        IsRemoteFolderNavigationMode = false;
        OnPropertyChanged(nameof(HasRemoteInstallerVersions));
        _latestRemoteFolderName = string.Empty;
        SelectedIconKey = "IconPackage";
        Icon = "📦";
        CustomIconLightPath = null;
        CustomIconDarkPath = null;
        IsIconPickerOpen = false;
        UseSeparateThemeIcons = false;
        IsIconPickerTargetDark = false;
        Title = "Nuovo Processo";
        IsEditMode = false;
        CanDelete = false;
        CreatedProcess = null;
        ValidationError = null;
        DialogResult = null;
    }

    public void InitializeForEdit(DeploymentProcess process)
    {
        _existingProcessId = process.Id;
        ProcessName = process.Name;
        Description = process.Description;
        SelectedProcessKind = process.Kind;
        InstallerSourceMode = InferInstallerSourceMode(process);
        FilePath = InstallerSourceMode == InstallerSourceMode.StaticLocal ? process.RelativePath : string.Empty;
        Arguments = process.Arguments;
        ScriptContent = process.ScriptContent;
        RunAsAdmin = process.RunAsAdmin;
        RequiresInternet = process.RequiresInternet;
        DownloadBaseFolderUrl = process.DownloadBaseFolderUrl;
        DownloadUrl = process.DownloadUrl;
        DownloadSelectedFileName = !string.IsNullOrWhiteSpace(process.DownloadSelectedFileName)
            ? process.DownloadSelectedFileName
            : process.DownloadSelectedFileTemplate;
        DownloadSelectedVersion = !string.IsNullOrWhiteSpace(process.DownloadVersionFolderName)
            ? process.DownloadVersionFolderName
            : string.Empty;
        RemoteInstallerFiles.Clear();
        RemoteInstallerVersions.Clear();
        IsRemoteFolderNavigationMode = false;
        OnPropertyChanged(nameof(HasRemoteInstallerVersions));
        _latestRemoteFolderName = string.Empty;
        SelectedIconKey = process.IconKey;
        Icon = string.IsNullOrWhiteSpace(process.Icon) ? "📦" : process.Icon;
        CustomIconLightPath = process.CustomIconLightPath;
        CustomIconDarkPath = process.CustomIconDarkPath;
        IsIconPickerOpen = false;
        UseSeparateThemeIcons = false;
        IsIconPickerTargetDark = false;
        Title = "Modifica Processo";
        IsEditMode = true;
        CanDelete = process.IsUserCreated;
        CreatedProcess = null;
        ValidationError = null;
        DialogResult = null;

        if (IsRequiresInternetLocked)
        {
            RequiresInternet = true;
        }

        if (InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
            !string.IsNullOrWhiteSpace(DownloadBaseFolderUrl) &&
            _authService?.IsAuthenticated == true)
        {
            _ = RefreshRemoteInstallerFilesAsync();
        }
    }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog();

        if (SelectedProcessKind == ProcessKind.Installer)
        {
            dialog.Filter = "File Installer (*.exe;*.msi;*.zip)|*.exe;*.msi;*.zip|Tutti i file (*.*)|*.*";
            dialog.Title = "Seleziona File Installer";
        }
        else if (SelectedProcessKind == ProcessKind.PowerShellScript)
        {
            dialog.Filter = "Script PowerShell (*.ps1)|*.ps1|Tutti i file (*.*)|*.*";
            dialog.Title = "Seleziona Script PowerShell";
        }
        else if (SelectedProcessKind == ProcessKind.BatchScript)
        {
            dialog.Filter = "Script Batch (*.bat;*.cmd)|*.bat;*.cmd|Tutti i file (*.*)|*.*";
            dialog.Title = "Seleziona Script Batch";
        }
        else if (SelectedProcessKind == ProcessKind.BashScript)
        {
            dialog.Filter = "Script Bash (*.sh)|*.sh|Tutti i file (*.*)|*.*";
            dialog.Title = "Seleziona Script Bash";
        }

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }

    private void Save()
    {
        if (!ValidateInput())
        {
            return;
        }

        var process = new DeploymentProcess
        {
            Id = _existingProcessId ?? Guid.NewGuid().ToString("N"),
            Name = ProcessName.Trim(),
            Description = Description.Trim(),
            Kind = SelectedProcessKind,
            Arguments = Arguments.Trim(),
            RunAsAdmin = RunAsAdmin,
            RequiresInternet = RequiresInternet,
            IsRequired = false,
            IconKey = SelectedIconKey,
            Icon = Icon,
            CustomIconLightPath = CustomIconLightPath,
            CustomIconDarkPath = CustomIconDarkPath,
            IsUserCreated = true,
            EnabledByDefault = true
        };

        if (IsInstallerMode)
        {
            process.InstallerSourceMode = InstallerSourceMode;
            process.RelativePath = InstallerSourceMode == InstallerSourceMode.StaticLocal
                ? FilePath
                : BuildDefaultInstallerCacheRelativePath(process.Id);

            process.DownloadUrl = InstallerSourceMode == InstallerSourceMode.StaticWeb
                ? DownloadUrl.Trim()
                : string.Empty;

            process.DownloadBaseFolderUrl = InstallerSourceMode == InstallerSourceMode.DynamicWeb
                ? DownloadBaseFolderUrl.Trim()
                : string.Empty;

            process.DownloadSelectedFileName = InstallerSourceMode == InstallerSourceMode.DynamicWeb
                ? DownloadSelectedFileName.Trim()
                : string.Empty;

            process.DownloadSelectedFileTemplate = InstallerSourceMode == InstallerSourceMode.DynamicWeb
                ? BuildDownloadTemplate(process.DownloadSelectedFileName)
                : string.Empty;

            process.DownloadPickLatestFolderByName = InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
                                                    !string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl);

            var selectedVersion = DownloadSelectedVersion?.Trim() ?? string.Empty;
            process.DownloadUseLatestVersion = false;
            process.DownloadVersionFolderName = selectedVersion;

            process.RequiresAuth = InstallerSourceMode != InstallerSourceMode.StaticLocal &&
                                   RequiresAuthForUrl(InstallerSourceMode == InstallerSourceMode.DynamicWeb ? process.DownloadBaseFolderUrl : process.DownloadUrl);
        }
        else if (IsScriptMode)
        {
            if (!string.IsNullOrWhiteSpace(ScriptContent))
            {
                process.ScriptContent = ScriptContent.Trim();
                process.RelativePath = string.Empty;
            }
            else
            {
                process.RelativePath = FilePath;
                process.ScriptContent = string.Empty;
            }
        }

        CreatedProcess = process;
        DialogResult = true;
    }

    private void OpenIconPicker()
    {
        LoadLibraryIcons();
        IsIconPickerOpen = true;
        ValidationError = null;
    }

    private void CloseIconPicker() => IsIconPickerOpen = false;

    private void SelectEmojiIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return;
        Icon = icon;
        CustomIconLightPath = null;
        CustomIconDarkPath = null;
        IsIconPickerOpen = false;
        ValidationError = null;
    }

    private void ImportLibraryIcon()
    {
        ValidationError = null;

        var path = PickIconFile("Seleziona file icona");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (_presetIconService == null)
            {
                ValidationError = "Servizio icone non disponibile.";
                return;
            }

            var item = _presetIconService.ImportLibraryIcon(path);
            LibraryIcons.Insert(0, item);
            SelectLibraryIcon(item);
        }
        catch (Exception ex)
        {
            ValidationError = $"Errore durante l'import icona: {ex.Message}";
        }
    }

    private void SelectLibraryIcon(PresetIconLibraryItem? item)
    {
        if (item is null) return;
        if (string.IsNullOrWhiteSpace(item.LightPath) && string.IsNullOrWhiteSpace(item.DarkPath)) return;

        if (!UseSeparateThemeIcons)
        {
            var candidate = !string.IsNullOrWhiteSpace(item.LightPath) ? item.LightPath : item.DarkPath;
            CustomIconLightPath = candidate;
            CustomIconDarkPath = !string.IsNullOrWhiteSpace(item.DarkPath) ? item.DarkPath : candidate;
            IsIconPickerOpen = false;
            ValidationError = null;
            return;
        }

        if (IsIconPickerTargetDark)
        {
            CustomIconDarkPath = !string.IsNullOrWhiteSpace(item.DarkPath) ? item.DarkPath : item.LightPath;
        }
        else
        {
            CustomIconLightPath = !string.IsNullOrWhiteSpace(item.LightPath) ? item.LightPath : item.DarkPath;
        }

        ValidationError = null;
    }

    private void RemoveCustomIcon()
    {
        ValidationError = null;
        CustomIconLightPath = null;
        CustomIconDarkPath = null;
    }

    private void SetIconPickerTargetLight() => IsIconPickerTargetDark = false;

    private void SetIconPickerTargetDark() => IsIconPickerTargetDark = true;

    private static string? PickIconFile(string title)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.ico",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void LoadLibraryIcons()
    {
        LibraryIcons.Clear();
        if (_presetIconService == null) return;

        foreach (var item in _presetIconService.GetLibraryIcons())
        {
            LibraryIcons.Add(item);
        }
    }

    private void Cancel()
    {
        ValidationError = null;
        CreatedProcess = null;
        DialogResult = false;
    }

    private void RequestDelete()
    {
        ValidationError = null;
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(ProcessName))
        {
            ValidationError = "Il nome del processo è obbligatorio.";
            return false;
        }

        if (IsInstallerMode)
        {
            if (InstallerSourceMode == InstallerSourceMode.StaticLocal)
            {
                if (string.IsNullOrWhiteSpace(FilePath))
                {
                    ValidationError = "Il percorso del file è obbligatorio per gli installer locali.";
                    return false;
                }

                if (!File.Exists(FilePath))
                {
                    ValidationError = "Il file specificato non esiste.";
                    return false;
                }

                var ext = Path.GetExtension(FilePath).ToLowerInvariant();
                if (ext != ".exe" && ext != ".msi" && ext != ".zip")
                {
                    ValidationError = "L'installer deve essere un file .exe, .msi o .zip.";
                    return false;
                }
            }
            else if (InstallerSourceMode == InstallerSourceMode.StaticWeb)
            {
                if (string.IsNullOrWhiteSpace(DownloadUrl))
                {
                    ValidationError = "Inserisci un link diretto all'installer (.exe).";
                    return false;
                }

                if (!Uri.TryCreate(DownloadUrl.Trim(), UriKind.Absolute, out var uri))
                {
                    ValidationError = "Link non valido.";
                    return false;
                }

                if (!uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    ValidationError = "Il link deve puntare a un file .exe.";
                    return false;
                }
            }
            else if (InstallerSourceMode == InstallerSourceMode.DynamicWeb)
            {
                if (string.IsNullOrWhiteSpace(DownloadBaseFolderUrl))
                {
                    ValidationError = "Inserisci una cartella web (es. .../Aggiornamenti/Retail/).";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(DownloadSelectedFileName))
                {
                    ValidationError = "Seleziona l'installer remoto da scaricare.";
                    return false;
                }
            }
        }
        else if (IsScriptMode)
        {
            var hasContent = !string.IsNullOrWhiteSpace(ScriptContent);
            var hasFile = !string.IsNullOrWhiteSpace(FilePath);

            if (!hasContent && !hasFile)
            {
                ValidationError = "È richiesto il contenuto dello script o il percorso del file.";
                return false;
            }

            if (hasFile && !File.Exists(FilePath))
            {
                ValidationError = "Il file script specificato non esiste.";
                return false;
            }
        }

        ValidationError = null;
        return true;
    }

    private bool CanRefreshRemoteInstallerFiles() =>
        InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
        !string.IsNullOrWhiteSpace(DownloadBaseFolderUrl);

    private async Task RefreshRemoteInstallerFilesAsync()
    {
        ValidationError = null;

        if (_downloadDirectoryListingService is null || _authService is null)
        {
            ValidationError = "Funzione download non disponibile.";
            return;
        }

        if (!_authService.IsAuthenticated)
        {
            ValidationError = "Effettua il login prima di caricare gli installer dal portale.";
            return;
        }

        try
        {
            var url = DownloadBaseFolderUrl.Trim();
            _log?.Info($"Loading remote installers list from: {url}");

            var folders = await _downloadDirectoryListingService.ListSubfoldersAsync(url, pickLatestFolderByName: true);
            LatestFolderExeListing? listing;

            if (folders.Count > 0)
            {
                var looksLikeVersions = LooksLikeVersionFolders(folders);
                IsRemoteFolderNavigationMode = !looksLikeVersions;

                RemoteInstallerVersions.Clear();
                if (IsRemoteFolderNavigationMode)
                {
                    foreach (var f in folders) RemoteInstallerVersions.Add(f);
                }
                else
                {
                    foreach (var f in folders.Reverse()) RemoteInstallerVersions.Add(f);
                }

                _latestRemoteFolderName = folders.LastOrDefault() ?? string.Empty;

                var selectedVersion = DownloadSelectedVersion;
                if (IsRemoteFolderNavigationMode)
                {
                    if (string.IsNullOrWhiteSpace(selectedVersion) ||
                        !folders.Any(v => string.Equals(v, selectedVersion, StringComparison.OrdinalIgnoreCase)))
                    {
                        _suppressVersionAutoLoad = true;
                        DownloadSelectedVersion = folders.FirstOrDefault() ?? string.Empty;
                        _suppressVersionAutoLoad = false;
                    }

                    RemoteInstallerFiles.Clear();
                    DownloadSelectedFileName = string.Empty;
                    ValidationError = null;
                    OpenSelectedRemoteFolderCommand.NotifyCanExecuteChanged();
                    NavigateUpRemoteFolderCommand.NotifyCanExecuteChanged();
                    return;
                }

                if (string.IsNullOrWhiteSpace(selectedVersion) ||
                    !folders.Any(v => string.Equals(v, selectedVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedVersion = _latestRemoteFolderName;
                    _suppressVersionAutoLoad = true;
                    DownloadSelectedVersion = selectedVersion;
                    _suppressVersionAutoLoad = false;
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUri))
                {
                    ValidationError = "URL non valido.";
                    return;
                }

                var folderUri = new Uri(baseUri, selectedVersion.Trim('/').Trim() + "/");
                listing = await _downloadDirectoryListingService.GetFolderExeListingAsync(folderUri.ToString());
            }
            else
            {
                RemoteInstallerVersions.Clear();
                IsRemoteFolderNavigationMode = false;
                listing = await _downloadDirectoryListingService.GetFolderExeListingAsync(url);
                _latestRemoteFolderName = listing?.FolderName ?? string.Empty;
            }

            RemoteInstallerFiles.Clear();

            foreach (var f in listing?.ExeFiles ?? Array.Empty<string>())
                RemoteInstallerFiles.Add(f);

            if (RemoteInstallerFiles.Count == 0)
            {
                ValidationError = "Nessun installer .exe trovato nella cartella.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(DownloadSelectedFileName) &&
                !RemoteInstallerFiles.Any(f => string.Equals(f, DownloadSelectedFileName, StringComparison.OrdinalIgnoreCase)))
            {
                DownloadSelectedFileName = RemoteInstallerFiles.FirstOrDefault() ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            _log?.Error("Failed to load remote installers list", ex);
            ValidationError = "Errore durante il caricamento elenco installer.";
        }
    }

    private async Task OnDownloadSelectedVersionChangedAsync()
    {
        if (_downloadDirectoryListingService is null) return;
        if (_authService is null || !_authService.IsAuthenticated) return;
        if (InstallerSourceMode != InstallerSourceMode.DynamicWeb) return;
        if (IsRemoteFolderNavigationMode) return;
        if (RemoteInstallerVersions.Count == 0) return;
        if (string.IsNullOrWhiteSpace(DownloadBaseFolderUrl)) return;

        var selected = DownloadSelectedVersion;
        if (string.IsNullOrWhiteSpace(selected)) return;

        if (!Uri.TryCreate(DownloadBaseFolderUrl.Trim(), UriKind.Absolute, out var baseUri))
            return;

        _remoteInstallerLoadCts?.Cancel();
        _remoteInstallerLoadCts?.Dispose();
        _remoteInstallerLoadCts = new CancellationTokenSource();
        var ct = _remoteInstallerLoadCts.Token;

        try
        {
            var folderUri = new Uri(baseUri, selected.Trim('/').Trim() + "/");
            var listing = await _downloadDirectoryListingService.GetFolderExeListingAsync(folderUri.ToString(), ct);
            if (ct.IsCancellationRequested) return;

            RemoteInstallerFiles.Clear();
            foreach (var f in listing?.ExeFiles ?? Array.Empty<string>())
                RemoteInstallerFiles.Add(f);

            if (RemoteInstallerFiles.Count == 0)
            {
                ValidationError = "Nessun installer .exe trovato nella cartella.";
                return;
            }

            ValidationError = null;
            if (!string.IsNullOrWhiteSpace(DownloadSelectedFileName) &&
                !RemoteInstallerFiles.Any(f => string.Equals(f, DownloadSelectedFileName, StringComparison.OrdinalIgnoreCase)))
            {
                DownloadSelectedFileName = RemoteInstallerFiles.FirstOrDefault() ?? string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log?.Error("Failed to load remote installer files for selected version", ex);
            ValidationError = "Errore durante il caricamento elenco installer.";
        }
    }

    private bool CanOpenSelectedRemoteFolder() =>
        InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
        IsRemoteFolderNavigationMode &&
        !string.IsNullOrWhiteSpace(DownloadBaseFolderUrl) &&
        !string.IsNullOrWhiteSpace(DownloadSelectedVersion);

    private async Task OpenSelectedRemoteFolderAsync()
    {
        if (!CanOpenSelectedRemoteFolder()) return;
        if (!Uri.TryCreate(DownloadBaseFolderUrl.Trim(), UriKind.Absolute, out var baseUri)) return;

        var nextUri = new Uri(baseUri, DownloadSelectedVersion.Trim('/').Trim() + "/");
        DownloadBaseFolderUrl = nextUri.ToString();
        DownloadSelectedVersion = string.Empty;
        await RefreshRemoteInstallerFilesAsync();
    }

    private bool CanNavigateUpRemoteFolderAsync() =>
        InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
        IsRemoteFolderNavigationMode &&
        TryGetParentFolderUrl(DownloadBaseFolderUrl) is not null;

    private async Task NavigateUpRemoteFolderAsync()
    {
        var parent = TryGetParentFolderUrl(DownloadBaseFolderUrl);
        if (parent is null) return;
        DownloadBaseFolderUrl = parent;
        DownloadSelectedVersion = string.Empty;
        await RefreshRemoteInstallerFilesAsync();
    }

    private static bool LooksLikeVersionFolders(IReadOnlyList<string> folders)
    {
        if (folders.Count < 3) return false;
        var matches = folders.Count(f => VersionFolderRegex.IsMatch((f ?? string.Empty).Trim()));
        return matches >= Math.Max(3, (int)Math.Ceiling(folders.Count * 0.6));
    }

    private static string? TryGetParentFolderUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;

        var path = uri.AbsolutePath;
        if (path.EndsWith("/./", StringComparison.OrdinalIgnoreCase)) path = path[..^3] + "/";
        if (path.EndsWith("/.", StringComparison.OrdinalIgnoreCase)) path = path[..^2] + "/";
        path = path.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path)) return null;

        var idx = path.LastIndexOf('/');
        if (idx <= 0) return null;
        var parentPath = path[..idx] + "/";

        var builder = new UriBuilder(uri) { Path = parentPath, Query = string.Empty, Fragment = string.Empty };
        return builder.Uri.ToString();
    }

    private string BuildDownloadTemplate(string selectedFileName)
    {
        if (string.IsNullOrWhiteSpace(selectedFileName)) return string.Empty;
        if (string.IsNullOrWhiteSpace(_latestRemoteFolderName)) return selectedFileName;

        return selectedFileName.Contains(_latestRemoteFolderName, StringComparison.OrdinalIgnoreCase)
            ? selectedFileName.Replace(_latestRemoteFolderName, "{VERSION}", StringComparison.OrdinalIgnoreCase)
            : selectedFileName;
    }

    private static InstallerSourceMode InferInstallerSourceMode(DeploymentProcess process)
    {
        if (process.Kind != ProcessKind.Installer) return InstallerSourceMode.StaticLocal;
        if (!string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl)) return InstallerSourceMode.DynamicWeb;
        if (!string.IsNullOrWhiteSpace(process.DownloadUrl)) return InstallerSourceMode.StaticWeb;
        return process.InstallerSourceMode;
    }

    private static string BuildDefaultInstallerCacheRelativePath(string processId) =>
        Path.Combine("Data", "installers", processId, "installer.exe");

    private static bool RequiresAuthForUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return string.Equals(u.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase);
    }
}

public record IconOption(string Key, string DisplayName);
