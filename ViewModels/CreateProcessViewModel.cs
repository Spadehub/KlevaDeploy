using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using Microsoft.Win32;

namespace KlevaDeploy.ViewModels;

public sealed class CreateProcessViewModel : ObservableObject
{
    private const string LatestVersionLabel = "Ultima disponibile";
    private readonly IAuthService? _authService;
    private readonly IDownloadDirectoryListingService? _downloadDirectoryListingService;
    private readonly ILogService? _log;
    private string _latestRemoteFolderName = string.Empty;

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

    private string _downloadSelectedVersion = LatestVersionLabel;
    public string DownloadSelectedVersion
    {
        get => _downloadSelectedVersion;
        set => SetProperty(ref _downloadSelectedVersion, value);
    }

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
            RefreshRemoteInstallerFilesCommand.NotifyCanExecuteChanged();
            ValidationError = null;
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

    public bool IsInstallerMode => SelectedProcessKind == ProcessKind.Installer;
    public bool IsScriptMode =>
        SelectedProcessKind == ProcessKind.PowerShellScript ||
        SelectedProcessKind == ProcessKind.BatchScript ||
        SelectedProcessKind == ProcessKind.BashScript;

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

    public event EventHandler? DeleteRequested;

    public CreateProcessViewModel(IAuthService? authService = null, IDownloadDirectoryListingService? downloadDirectoryListingService = null, ILogService? log = null)
    {
        _authService = authService;
        _downloadDirectoryListingService = downloadDirectoryListingService;
        _log = log;

        BrowseFileCommand = new RelayCommand(BrowseFile);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(RequestDelete);
        RefreshRemoteInstallerFilesCommand = new AsyncRelayCommand(RefreshRemoteInstallerFilesAsync, CanRefreshRemoteInstallerFiles);
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
        DownloadSelectedVersion = LatestVersionLabel;
        RemoteInstallerFiles.Clear();
        RemoteInstallerVersions.Clear();
        _latestRemoteFolderName = string.Empty;
        SelectedIconKey = "IconPackage";
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
        DownloadSelectedVersion = (!process.DownloadUseLatestVersion && !string.IsNullOrWhiteSpace(process.DownloadVersionFolderName))
            ? process.DownloadVersionFolderName
            : LatestVersionLabel;
        RemoteInstallerFiles.Clear();
        RemoteInstallerVersions.Clear();
        _latestRemoteFolderName = string.Empty;
        SelectedIconKey = process.IconKey;
        Title = "Modifica Processo";
        IsEditMode = true;
        CanDelete = process.IsUserCreated;
        CreatedProcess = null;
        ValidationError = null;
        DialogResult = null;
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

            var useLatest = string.IsNullOrWhiteSpace(DownloadSelectedVersion) ||
                            string.Equals(DownloadSelectedVersion, LatestVersionLabel, StringComparison.OrdinalIgnoreCase);

            process.DownloadUseLatestVersion = useLatest;
            process.DownloadVersionFolderName = useLatest ? string.Empty : DownloadSelectedVersion.Trim();

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
                RemoteInstallerVersions.Clear();
                RemoteInstallerVersions.Add(LatestVersionLabel);
                foreach (var f in folders) RemoteInstallerVersions.Add(f);

                _latestRemoteFolderName = folders.LastOrDefault() ?? string.Empty;

                var selectedVersion = DownloadSelectedVersion;
                if (string.IsNullOrWhiteSpace(selectedVersion) ||
                    (!string.Equals(selectedVersion, LatestVersionLabel, StringComparison.OrdinalIgnoreCase) &&
                     !folders.Any(v => string.Equals(v, selectedVersion, StringComparison.OrdinalIgnoreCase))))
                {
                    DownloadSelectedVersion = LatestVersionLabel;
                    selectedVersion = LatestVersionLabel;
                }

                var versionToList = string.Equals(selectedVersion, LatestVersionLabel, StringComparison.OrdinalIgnoreCase)
                    ? _latestRemoteFolderName
                    : selectedVersion;

                var folderUrl = url.TrimEnd('/') + "/" + versionToList.Trim('/') + "/";
                listing = await _downloadDirectoryListingService.GetFolderExeListingAsync(folderUrl);
                _latestRemoteFolderName = versionToList;
            }
            else
            {
                RemoteInstallerVersions.Clear();
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
                DownloadSelectedFileName = string.Empty;
            }
        }
        catch (Exception ex)
        {
            _log?.Error("Failed to load remote installers list", ex);
            ValidationError = "Errore durante il caricamento elenco installer.";
        }
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
