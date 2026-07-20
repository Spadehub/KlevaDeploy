using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.Utilities;
using Microsoft.Win32;
using System.Collections.Specialized;

namespace KlevaDeploy.ViewModels;

public sealed class CreateProcessViewModel : ObservableObject
{
    private static readonly Regex VersionFolderRegex = new(@"^(?<year>\d{4})(?<suffix>[A-Za-z0-9]+)$", RegexOptions.Compiled);
    private readonly IAuthService? _authService;
    private readonly IDownloadDirectoryListingService? _downloadDirectoryListingService;
    private readonly IPresetIconService? _presetIconService;
    private readonly ILogService? _log;
    private readonly IPreferencesService? _prefsService;
    private readonly IProcessExecutionService? _processExecutionService;
    private readonly IUpdateService? _updateService;
    private readonly Func<Task>? _openLoginAsync;
    private string _latestRemoteFolderName = string.Empty;
    private string _existingDownloadSelectedFileTemplate = string.Empty;
    private string _persistedVersionFolderName = string.Empty;
    private string _loadedSavedVersionFolderName = string.Empty;
    private bool _forceLatestVersionOnRefresh;
    private CancellationTokenSource? _remoteInstallerLoadCts;
    private bool _suppressVersionAutoLoad;

    public IPreferencesService? PreferencesService => _prefsService;

    private string? _existingProcessId;
    public string? EditingProcessId => _existingProcessId;
    private string _editingProcessRelativePath = string.Empty;
    private string? _autoConfigTempProcessId;

    private bool _isSubProcessEditor;
    public bool IsSubProcessEditor
    {
        get => _isSubProcessEditor;
        set => SetProperty(ref _isSubProcessEditor, value);
    }

    private string _baselineSubProcessesJson = string.Empty;

    private bool _hasUnsavedSubProcessChanges;
    public bool HasUnsavedSubProcessChanges
    {
        get => _hasUnsavedSubProcessChanges;
        private set => SetProperty(ref _hasUnsavedSubProcessChanges, value);
    }

    private bool _isSubProcessChangesPromptOpen;
    public bool IsSubProcessChangesPromptOpen
    {
        get => _isSubProcessChangesPromptOpen;
        set => SetProperty(ref _isSubProcessChangesPromptOpen, value);
    }

    public string SubProcessChangesPromptMessage =>
        "Ci sono alcune modifiche ai sottoprocessi. Sei sicuro di voler annullare?";

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
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
            AutoGenerateInstallerFlowCommand.NotifyCanExecuteChanged();
            ValidationError = null;
        }
    }

    private string _filePath = string.Empty;
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (!SetProperty(ref _filePath, value)) return;
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
            AutoGenerateInstallerFlowCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _suppressExeInstallerModeSync;
    private ExeInstallerMode _selectedExeInstallerMode = ExeInstallerMode.Manual;
    public IReadOnlyList<ExeInstallerMode> AvailableExeInstallerModes { get; } = Enum.GetValues<ExeInstallerMode>();
    public ExeInstallerMode SelectedExeInstallerMode
    {
        get => _selectedExeInstallerMode;
        set
        {
            if (!SetProperty(ref _selectedExeInstallerMode, value)) return;
            if (_suppressExeInstallerModeSync) return;

            _suppressExeInstallerModeSync = true;
            try
            {
                Arguments = ApplyExeInstallerModeToArguments(Arguments, value);
            }
            finally
            {
                _suppressExeInstallerModeSync = false;
            }
        }
    }

    private string _arguments = string.Empty;
    public string Arguments
    {
        get => _arguments;
        set
        {
            if (!SetProperty(ref _arguments, value)) return;
            if (_suppressExeInstallerModeSync) return;

            var inferred = InferExeInstallerModeFromArguments(value);
            if (inferred == _selectedExeInstallerMode) return;

            _suppressExeInstallerModeSync = true;
            try
            {
                SetProperty(ref _selectedExeInstallerMode, inferred, nameof(SelectedExeInstallerMode));
            }
            finally
            {
                _suppressExeInstallerModeSync = false;
            }
        }
    }

    public ObservableCollection<SubProcessItem> SubProcesses { get; } = new();
    public bool HasSubProcesses => SubProcesses.Count > 0;
    public DeploymentProcess? EditorNavigationRootProcess { get; private set; }
    public CreateProcessViewModel? EditorNavigationSourceViewModel { get; private set; }

    private SubProcessItem? _selectedSubProcess;
    public SubProcessItem? SelectedSubProcess
    {
        get => _selectedSubProcess;
        set
        {
            if (!SetProperty(ref _selectedSubProcess, value)) return;
            RemoveSubProcessCommand.NotifyCanExecuteChanged();
            MoveSubProcessUpCommand.NotifyCanExecuteChanged();
            MoveSubProcessDownCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isAutoConfigOpen;
    public bool IsAutoConfigOpen
    {
        get => _isAutoConfigOpen;
        set
        {
            if (!SetProperty(ref _isAutoConfigOpen, value)) return;
            ImportAutoConfigProfileCommand.NotifyCanExecuteChanged();
            ExportAutoConfigProfileCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isAutoConfigBusy;
    public bool IsAutoConfigBusy
    {
        get => _isAutoConfigBusy;
        set
        {
            if (!SetProperty(ref _isAutoConfigBusy, value)) return;
            ImportAutoConfigProfileCommand.NotifyCanExecuteChanged();
            ExportAutoConfigProfileCommand.NotifyCanExecuteChanged();
        }
    }

    private string _autoConfigStatusText = string.Empty;
    public string AutoConfigStatusText
    {
        get => _autoConfigStatusText;
        set => SetProperty(ref _autoConfigStatusText, value);
    }

    public ObservableCollection<MsiPropertyItem> AutoConfigMsiProperties { get; } = new();

    private string _autoConfigSearchText = string.Empty;
    public string AutoConfigSearchText
    {
        get => _autoConfigSearchText;
        set
        {
            if (!SetProperty(ref _autoConfigSearchText, value)) return;
            OnPropertyChanged(nameof(FilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(EnabledFilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(DisabledFilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasEnabledAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasDisabledAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasEnabledAndDisabledAutoConfigMsiProperties));
        }
    }

    public IEnumerable<MsiPropertyItem> FilteredAutoConfigMsiProperties
    {
        get
        {
            var q = (AutoConfigSearchText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                return AutoConfigMsiProperties
                    .OrderByDescending(p => p.IsEnabled)
                    .ThenBy(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return AutoConfigMsiProperties
                .Where(p =>
                    (!string.IsNullOrWhiteSpace(p.Name) && p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(p.Value) && p.Value.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => p.IsEnabled)
                .ThenBy(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public IEnumerable<MsiPropertyItem> EnabledFilteredAutoConfigMsiProperties =>
        FilteredAutoConfigMsiProperties.Where(p => p.IsEnabled);

    public IEnumerable<MsiPropertyItem> DisabledFilteredAutoConfigMsiProperties =>
        FilteredAutoConfigMsiProperties.Where(p => !p.IsEnabled);

    private const string SilentMarker = "{SILENT}";
    private const string AutoMarker = "{AUTO}";
    private const string AutoExtractMainMarker = "{AUTOEXTRACT_MAIN_MSI}";
    private const string AutoExtractAllMarker = "{AUTOEXTRACT_ALL_MSI}";

    private static ExeInstallerMode InferExeInstallerModeFromArguments(string? rawArgs)
    {
        var text = rawArgs ?? string.Empty;
        if (text.Contains(AutoExtractAllMarker, StringComparison.OrdinalIgnoreCase))
            return ExeInstallerMode.AutoExtractAllMsis;
        if (text.Contains(AutoExtractMainMarker, StringComparison.OrdinalIgnoreCase))
            return ExeInstallerMode.AutoExtractMainMsi;
        if (text.Contains(AutoMarker, StringComparison.OrdinalIgnoreCase))
            return ExeInstallerMode.Auto;
        if (text.Contains(SilentMarker, StringComparison.OrdinalIgnoreCase))
            return ExeInstallerMode.Silent;
        return ExeInstallerMode.Manual;
    }

    private static string ApplyExeInstallerModeToArguments(string? rawArgs, ExeInstallerMode mode)
    {
        var cleaned = RemoveExeInstallerMarkers(rawArgs);
        if (mode == ExeInstallerMode.Manual)
            return cleaned;

        var marker = mode switch
        {
            ExeInstallerMode.Auto => AutoMarker,
            ExeInstallerMode.Silent => SilentMarker,
            ExeInstallerMode.AutoExtractMainMsi => AutoExtractMainMarker,
            ExeInstallerMode.AutoExtractAllMsis => AutoExtractAllMarker,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(marker))
            return cleaned;

        if (string.IsNullOrWhiteSpace(cleaned))
            return marker;

        return $"{marker} {cleaned}".Trim();
    }

    private static string RemoveExeInstallerMarkers(string? rawArgs)
    {
        var text = (rawArgs ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;

        text = text.Replace(SilentMarker, string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace(AutoMarker, string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace(AutoExtractMainMarker, string.Empty, StringComparison.OrdinalIgnoreCase);
        text = text.Replace(AutoExtractAllMarker, string.Empty, StringComparison.OrdinalIgnoreCase);

        while (text.Contains("  ", StringComparison.Ordinal))
            text = text.Replace("  ", " ", StringComparison.Ordinal);

        return text.Trim();
    }

    public bool HasEnabledAutoConfigMsiProperties => FilteredAutoConfigMsiProperties.Any(p => p.IsEnabled);
    public bool HasDisabledAutoConfigMsiProperties => FilteredAutoConfigMsiProperties.Any(p => !p.IsEnabled);
    public bool HasEnabledAndDisabledAutoConfigMsiProperties => HasEnabledAutoConfigMsiProperties && HasDisabledAutoConfigMsiProperties;

    private string _autoConfigError = string.Empty;
    public string AutoConfigError
    {
        get => _autoConfigError;
        set => SetProperty(ref _autoConfigError, value);
    }

    private string _scriptContent = string.Empty;
    public string ScriptContent
    {
        get => _scriptContent;
        set
        {
            if (!SetProperty(ref _scriptContent, value)) return;
            OnPropertyChanged(nameof(InlineScriptCommand));
            OnPropertyChanged(nameof(HasScriptContent));
            OnPropertyChanged(nameof(HasMultiLineScriptContent));
            OnPropertyChanged(nameof(HasSingleLineScriptContent));
            OnPropertyChanged(nameof(ScriptPreview));
            OnPropertyChanged(nameof(ScriptEditorHint));
        }
    }

    public string InlineScriptCommand
    {
        get => HasMultiLineScriptContent ? string.Empty : (_scriptContent ?? string.Empty).ReplaceLineEndings(" ").Trim();
        set
        {
            var normalized = (value ?? string.Empty).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
            while (normalized.Contains("  ", StringComparison.Ordinal))
                normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
            ScriptContent = normalized.Trim();
        }
    }

    public bool HasScriptContent => !string.IsNullOrWhiteSpace(_scriptContent);
    public bool HasMultiLineScriptContent => !string.IsNullOrWhiteSpace(_scriptContent) && _scriptContent.Contains('\n', StringComparison.Ordinal);
    public bool HasSingleLineScriptContent => HasScriptContent && !HasMultiLineScriptContent;

    public string ScriptPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_scriptContent))
                return string.Empty;

            var lines = _scriptContent.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            var preview = string.Join(Environment.NewLine, lines.Take(8));
            if (lines.Length > 8)
                preview += Environment.NewLine + "...";
            return preview;
        }
    }

    public string ScriptEditorHint =>
        HasMultiLineScriptContent
            ? "Multi-line script loaded. Use the editor window for full editing."
            : "Use the editor window for long-format scripts.";

    private string _installDirectory = string.Empty;
    public string InstallDirectory
    {
        get => _installDirectory;
        set => SetProperty(ref _installDirectory, value);
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

    private bool _portalAccessEnabled;
    public bool PortalAccessEnabled
    {
        get => _portalAccessEnabled;
        set
        {
            if (!SetProperty(ref _portalAccessEnabled, value)) return;
            OnPropertyChanged(nameof(IsPortalPickerVisible));
            if (_portalAccessEnabled)
            {
                if (string.IsNullOrWhiteSpace(SelectedPortalId))
                    SelectedPortalId = _prefsService?.Preferences.SelectedPortalId;
            }
            else
                SelectedPortalId = null;
        }
    }

    public bool IsPortalPickerVisible => PortalAccessEnabled;

    private string? _selectedPortalId;
    public string? SelectedPortalId
    {
        get => _selectedPortalId;
        set => SetProperty(ref _selectedPortalId, value);
    }

    public ObservableCollection<PortalOption> AvailablePortals { get; } = new();

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
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
        }
    }

    private string _downloadUrl = string.Empty;
    public string DownloadUrl
    {
        get => _downloadUrl;
        set
        {
            if (!SetProperty(ref _downloadUrl, value)) return;
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
        }
    }

    private string _downloadSelectedFileName = string.Empty;
    public string DownloadSelectedFileName
    {
        get => _downloadSelectedFileName;
        set
        {
            if (!SetProperty(ref _downloadSelectedFileName, value)) return;
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
        }
    }

    public ObservableCollection<string> RemoteInstallerFiles { get; } = new();

    public ObservableCollection<string> RemoteInstallerVersions { get; } = new();

    public bool HasRemoteInstallerVersions => RemoteInstallerVersions.Count > 0;
    public bool IsRemoteVersionPickerMode => HasRemoteInstallerVersions && !IsRemoteFolderNavigationMode;

    private string _downloadSelectedVersion = string.Empty;
    public string DownloadSelectedVersion
    {
        get => _downloadSelectedVersion;
        set
        {
            if (!SetProperty(ref _downloadSelectedVersion, value)) return;
            if (!_suppressVersionAutoLoad && !IsRemoteFolderNavigationMode)
                _persistedVersionFolderName = (_downloadSelectedVersion ?? string.Empty).Trim();
            if (!_suppressVersionAutoLoad)
                _ = OnDownloadSelectedVersionChangedAsync();
            OpenSelectedRemoteFolderCommand.NotifyCanExecuteChanged();
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _isInstallerAutoUpdateEnabled = true;
    public bool IsInstallerAutoUpdateEnabled
    {
        get => _isInstallerAutoUpdateEnabled;
        set
        {
            if (!SetProperty(ref _isInstallerAutoUpdateEnabled, value)) return;

            if (_isInstallerAutoUpdateEnabled &&
                InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
                !IsRemoteFolderNavigationMode &&
                !string.IsNullOrWhiteSpace(_latestRemoteFolderName))
            {
                _forceLatestVersionOnRefresh = true;
                DownloadSelectedVersion = _latestRemoteFolderName;
                _persistedVersionFolderName = _latestRemoteFolderName;
            }
        }
    }

    public void PrepareAutoUpdateCheck()
    {
        _forceLatestVersionOnRefresh = true;
    }

    private bool _isRemoteFolderNavigationMode;
    public bool IsRemoteFolderNavigationMode
    {
        get => _isRemoteFolderNavigationMode;
        set
        {
            if (!SetProperty(ref _isRemoteFolderNavigationMode, value)) return;
            OnPropertyChanged(nameof(RemoteFolderPickerLabel));
            OnPropertyChanged(nameof(IsRemoteVersionPickerMode));
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
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
            AutoGenerateInstallerFlowCommand.NotifyCanExecuteChanged();
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
        "📦", "🧩", "🖥️", "💻", "🪟", "⚙️", "🧰", "🔧",
        "⬇️", "⬆️", "🔄", "▶️", "⏸️", "⏳", "🔁", "🧪",
        "🌐", "☁️", "🔌", "🖧", "📡", "🛡️", "🔐", "🔒", "🔓", "🔑",
        "🗄️", "🧾", "🧱", "🧠", "📁", "📂", "🗂️", "💾",
        "🧹", "🗑️", "📋", "🧷", "📝", "🧨",
        "✅", "⚠️", "❌", "ℹ️"
    };

    public ObservableCollection<PresetIconLibraryItem> LibraryIcons { get; } = new();

    public bool IsInstallerMode => SelectedProcessKind == ProcessKind.Installer;
    public bool IsScriptMode =>
        SelectedProcessKind == ProcessKind.PowerShellScript ||
        SelectedProcessKind == ProcessKind.BatchScript ||
        SelectedProcessKind == ProcessKind.BashScript;

    public bool IsRequiresInternetLocked => IsInstallerMode && InstallerSourceMode != InstallerSourceMode.StaticLocal;
    public bool IsRequiresInternetUserEditable => !IsRequiresInternetLocked;
    public IProcessExecutionService? ProcessExecutionService => _processExecutionService;

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
    public IAsyncRelayCommand OpenAutoConfigCommand { get; }
    public IAsyncRelayCommand AutoGenerateInstallerFlowCommand { get; }
    public IRelayCommand ApplyAutoConfigCommand { get; }
    public IRelayCommand CloseAutoConfigCommand { get; }
    public IRelayCommand ClearAutoConfigSearchCommand { get; }
    public IRelayCommand ImportAutoConfigProfileCommand { get; }
    public IRelayCommand ExportAutoConfigProfileCommand { get; }
    public IRelayCommand<SubProcessItem?> RemoveSubProcessCommand { get; }
    public IRelayCommand<SubProcessItem?> MoveSubProcessUpCommand { get; }
    public IRelayCommand<SubProcessItem?> MoveSubProcessDownCommand { get; }
    public IRelayCommand ImportProcessCommand { get; }
    public IRelayCommand ExportProcessCommand { get; }
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

    public IRelayCommand DiscardSubProcessChangesAndCloseCommand { get; }
    public IRelayCommand DismissSubProcessChangesPromptCommand { get; }
    public IRelayCommand SaveFromSubProcessChangesPromptCommand { get; }

    public event EventHandler? DeleteRequested;

    public CreateProcessViewModel CreateChildViewModel()
    {
        var vm = new CreateProcessViewModel(_authService, _downloadDirectoryListingService, _prefsService, _log, _presetIconService, _processExecutionService, _updateService, _openLoginAsync)
        {
            IsSubProcessEditor = true
        };
        vm.EditorNavigationSourceViewModel = this;
        vm.EditorNavigationRootProcess = BuildEditorProcessSnapshot(includeSubProcesses: true);
        return vm;
    }

    public CreateProcessViewModel(IAuthService? authService = null, IDownloadDirectoryListingService? downloadDirectoryListingService = null, IPreferencesService? prefsService = null, ILogService? log = null, IPresetIconService? presetIconService = null, IProcessExecutionService? processExecutionService = null, IUpdateService? updateService = null, Func<Task>? openLoginAsync = null)
    {
        _authService = authService;
        _downloadDirectoryListingService = downloadDirectoryListingService;
        _prefsService = prefsService;
        _log = log;
        _presetIconService = presetIconService;
        _processExecutionService = processExecutionService;
        _updateService = updateService;
        _openLoginAsync = openLoginAsync;

        LoadPortalsFromPreferences();

        SubProcesses.CollectionChanged += OnSubProcessesCollectionChanged;

        RemoteInstallerVersions.CollectionChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(HasRemoteInstallerVersions));
            OnPropertyChanged(nameof(IsRemoteVersionPickerMode));
        };

        BrowseFileCommand = new RelayCommand(BrowseFile);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
        DeleteCommand = new RelayCommand(RequestDelete);
        OpenAutoConfigCommand = new AsyncRelayCommand(OpenAutoConfigAsync, CanOpenAutoConfig);
        AutoGenerateInstallerFlowCommand = new AsyncRelayCommand(AutoGenerateInstallerFlowAsync, CanAutoGenerateInstallerFlow);
        ApplyAutoConfigCommand = new RelayCommand(ApplyAutoConfig, CanApplyAutoConfig);
        CloseAutoConfigCommand = new RelayCommand(CloseAutoConfig);
        ClearAutoConfigSearchCommand = new RelayCommand(() => AutoConfigSearchText = string.Empty);
        ImportAutoConfigProfileCommand = new RelayCommand(ImportAutoConfigProfile, CanImportAutoConfigProfile);
        ExportAutoConfigProfileCommand = new RelayCommand(ExportAutoConfigProfile, CanExportAutoConfigProfile);
        RemoveSubProcessCommand = new RelayCommand<SubProcessItem?>(RemoveSubProcess, CanRemoveSubProcess);
        MoveSubProcessUpCommand = new RelayCommand<SubProcessItem?>(MoveSubProcessUp, CanMoveSubProcessUp);
        MoveSubProcessDownCommand = new RelayCommand<SubProcessItem?>(MoveSubProcessDown, CanMoveSubProcessDown);
        ImportProcessCommand = new RelayCommand(ImportProcess);
        ExportProcessCommand = new RelayCommand(ExportProcess);
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
        DiscardSubProcessChangesAndCloseCommand = new RelayCommand(DiscardSubProcessChangesAndClose);
        DismissSubProcessChangesPromptCommand = new RelayCommand(() => IsSubProcessChangesPromptOpen = false);
        SaveFromSubProcessChangesPromptCommand = new RelayCommand(SaveFromSubProcessChangesPrompt);

        AutoConfigMsiProperties.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
            {
                foreach (var it in e.NewItems.OfType<MsiPropertyItem>())
                {
                    it.PropertyChanged += OnAutoConfigItemPropertyChanged;
                }
            }

            if (e.OldItems is not null)
            {
                foreach (var it in e.OldItems.OfType<MsiPropertyItem>())
                {
                    it.PropertyChanged -= OnAutoConfigItemPropertyChanged;
                }
            }

            ApplyAutoConfigCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(EnabledFilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(DisabledFilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasEnabledAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasDisabledAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasEnabledAndDisabledAutoConfigMsiProperties));
        };
    }

    private void OnSubProcessesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSubProcesses));

        if (e.NewItems is not null)
        {
            foreach (var it in e.NewItems.OfType<SubProcessItem>())
            {
                it.PropertyChanged += OnSubProcessItemPropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var it in e.OldItems.OfType<SubProcessItem>())
            {
                it.PropertyChanged -= OnSubProcessItemPropertyChanged;
            }
        }

        RemoveSubProcessCommand?.NotifyCanExecuteChanged();
        MoveSubProcessUpCommand?.NotifyCanExecuteChanged();
        MoveSubProcessDownCommand?.NotifyCanExecuteChanged();

        RecomputeSubProcessDirtyState();
    }

    private void OnSubProcessItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RecomputeSubProcessDirtyState();
    }

    public void InitializeNew()
    {
        _existingProcessId = null;
        _editingProcessRelativePath = string.Empty;
        _autoConfigTempProcessId = null;
        ProcessName = string.Empty;
        Description = string.Empty;
        SelectedProcessKind = ProcessKind.Installer;
        InstallerSourceMode = InstallerSourceMode.StaticLocal;
        FilePath = string.Empty;
        Arguments = string.Empty;
        SubProcesses.Clear();
        SelectedSubProcess = null;
        MoveSubProcessUpCommand.NotifyCanExecuteChanged();
        MoveSubProcessDownCommand.NotifyCanExecuteChanged();
        RemoveSubProcessCommand.NotifyCanExecuteChanged();
        IsAutoConfigOpen = false;
        IsAutoConfigBusy = false;
        AutoConfigStatusText = string.Empty;
        AutoConfigMsiProperties.Clear();
        AutoConfigSearchText = string.Empty;
        AutoConfigError = string.Empty;
        ScriptContent = string.Empty;
        InstallDirectory = string.Empty;
        RunAsAdmin = false;
        RequiresInternet = false;
        PortalAccessEnabled = false;
        SelectedPortalId = _prefsService?.Preferences.SelectedPortalId;
        DownloadBaseFolderUrl = string.Empty;
        DownloadUrl = string.Empty;
        DownloadSelectedFileName = string.Empty;
        DownloadSelectedVersion = string.Empty;
        IsInstallerAutoUpdateEnabled = true;
        RemoteInstallerFiles.Clear();
        RemoteInstallerVersions.Clear();
        IsRemoteFolderNavigationMode = false;
        OnPropertyChanged(nameof(HasRemoteInstallerVersions));
        _latestRemoteFolderName = string.Empty;
        _existingDownloadSelectedFileTemplate = string.Empty;
        _persistedVersionFolderName = string.Empty;
        _loadedSavedVersionFolderName = string.Empty;
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

        LoadPortalsFromPreferences();
        CaptureBaseline();
    }

    public void InitializeForEdit(DeploymentProcess process)
    {
        _existingProcessId = process.Id;
        _editingProcessRelativePath = process.RelativePath ?? string.Empty;
        _autoConfigTempProcessId = null;
        ProcessName = process.Name;
        Description = process.Description;
        SelectedProcessKind = process.Kind;
        InstallerSourceMode = InferInstallerSourceMode(process);
        FilePath = InstallerSourceMode == InstallerSourceMode.StaticLocal ? (process.RelativePath ?? string.Empty) : string.Empty;
        Arguments = process.Arguments;
        SubProcesses.Clear();
        if (process.SubProcesses is not null && process.SubProcesses.Count > 0)
        {
            foreach (var sp in process.SubProcesses)
            {
                if (sp.Process is not null)
                {
                    var cloned = CloneProcess(sp.Process);
                    if (!string.IsNullOrWhiteSpace(sp.Name)) cloned.Name = sp.Name.Trim();
                    if (!string.IsNullOrWhiteSpace(sp.RelativePath)) cloned.RelativePath = sp.RelativePath.Trim();
                    if (!string.IsNullOrWhiteSpace(sp.Arguments)) cloned.Arguments = sp.Arguments.Trim();
                    if (sp.RunAsAdmin.HasValue) cloned.RunAsAdmin = sp.RunAsAdmin.Value;
                    SubProcesses.Add(new SubProcessItem { Process = cloned });
                    continue;
                }

                var legacy = BuildLegacySubProcess(sp);
                if (legacy is not null)
                {
                    SubProcesses.Add(new SubProcessItem { Process = legacy });
                    continue;
                }

                var legacyItem = new SubProcessItem();
                legacyItem.Name = sp.Name ?? string.Empty;
                legacyItem.RelativePath = sp.RelativePath ?? string.Empty;
                legacyItem.Arguments = sp.Arguments ?? string.Empty;
                legacyItem.SubProcess.RunAsAdmin = sp.RunAsAdmin;
                SubProcesses.Add(legacyItem);
            }
        }
        SelectedSubProcess = SubProcesses.FirstOrDefault();
        MoveSubProcessUpCommand.NotifyCanExecuteChanged();
        MoveSubProcessDownCommand.NotifyCanExecuteChanged();
        RemoveSubProcessCommand.NotifyCanExecuteChanged();
        IsAutoConfigOpen = false;
        IsAutoConfigBusy = false;
        AutoConfigStatusText = string.Empty;
        AutoConfigMsiProperties.Clear();
        AutoConfigSearchText = string.Empty;
        AutoConfigError = string.Empty;
        ScriptContent = process.ScriptContent;
        InstallDirectory = process.InstallDirectory;
        RunAsAdmin = process.RunAsAdmin;
        RequiresInternet = process.RequiresInternet;
        PortalAccessEnabled = process.RequiresAuth;
        SelectedPortalId = !string.IsNullOrWhiteSpace(process.PortalId)
            ? process.PortalId
            : GuessPortalIdFromProcess(process);
        DownloadBaseFolderUrl = process.DownloadBaseFolderUrl;
        DownloadUrl = process.DownloadUrl;
        DownloadSelectedFileName = !string.IsNullOrWhiteSpace(process.DownloadSelectedFileName)
            ? process.DownloadSelectedFileName
            : process.DownloadSelectedFileTemplate;
        _existingDownloadSelectedFileTemplate = process.DownloadSelectedFileTemplate ?? string.Empty;
        IsInstallerAutoUpdateEnabled = process.DownloadUseLatestVersion;
        _loadedSavedVersionFolderName = NormalizeVersionFolderName(process.DownloadVersionFolderName);
        _persistedVersionFolderName = _loadedSavedVersionFolderName;
        DownloadSelectedVersion = _loadedSavedVersionFolderName;
        RemoteInstallerFiles.Clear();
        RemoteInstallerVersions.Clear();
        IsRemoteFolderNavigationMode = false;
        OnPropertyChanged(nameof(HasRemoteInstallerVersions));
        _latestRemoteFolderName = string.Empty;
        process.NormalizeIconRecursively();
        SelectedIconKey = process.IconKey;
        Icon = DeploymentProcess.ResolveBuiltInIcon(process.IconKey, process.Icon);
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
            _authService?.IsAuthenticatedForUrl(DownloadBaseFolderUrl) == true)
        {
            _ = RefreshRemoteInstallerFilesAsync();
        }

        if (InstallerSourceMode == InstallerSourceMode.DynamicWeb)
        {
            if (!string.IsNullOrWhiteSpace(DownloadSelectedVersion))
                RemoteInstallerVersions.Add(DownloadSelectedVersion);
            if (!string.IsNullOrWhiteSpace(DownloadSelectedFileName))
                RemoteInstallerFiles.Add(DownloadSelectedFileName);
        }

        LoadPortalsFromPreferences();
        CaptureBaseline();
    }

    private void CaptureBaseline()
    {
        _baselineSubProcessesJson = SerializeSubProcesses(SubProcesses);
        HasUnsavedSubProcessChanges = false;
        IsSubProcessChangesPromptOpen = false;
    }

    private void RecomputeSubProcessDirtyState()
    {
        if (IsSubProcessEditor) { HasUnsavedSubProcessChanges = false; return; }
        if (string.IsNullOrWhiteSpace(_baselineSubProcessesJson)) return;
        var current = SerializeSubProcesses(SubProcesses);
        HasUnsavedSubProcessChanges = !string.Equals(_baselineSubProcessesJson, current, StringComparison.Ordinal);
    }

    private static string SerializeSubProcesses(ObservableCollection<SubProcessItem> items)
    {
        var list = items
            .Select(s => new DeploymentSubProcess
            {
                Name = (s.SubProcess.Name ?? string.Empty).Trim(),
                Process = s.Process is null ? null : CloneProcess(s.Process),
                RelativePath = (s.SubProcess.RelativePath ?? string.Empty).Trim(),
                Arguments = (s.SubProcess.Arguments ?? string.Empty).Trim(),
                RunAsAdmin = s.SubProcess.RunAsAdmin
            })
            .ToList();

        return JsonSerializer.Serialize(list, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
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
            OpenAutoConfigCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnAutoConfigItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyAutoConfigCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FilteredAutoConfigMsiProperties));
        OnPropertyChanged(nameof(EnabledFilteredAutoConfigMsiProperties));
        OnPropertyChanged(nameof(DisabledFilteredAutoConfigMsiProperties));
        OnPropertyChanged(nameof(HasEnabledAutoConfigMsiProperties));
        OnPropertyChanged(nameof(HasDisabledAutoConfigMsiProperties));
        OnPropertyChanged(nameof(HasEnabledAndDisabledAutoConfigMsiProperties));
    }

    private bool CanOpenAutoConfig()
    {
        if (!IsInstallerMode) return false;
        if (TryResolveLocalInstallerPathForAutoConfig(out var cachedOrLocalPath))
        {
            var extExisting = Path.GetExtension(cachedOrLocalPath).ToLowerInvariant();
            if (extExisting is ".msi" or ".exe" or ".zip")
                return true;
        }
        if (InstallerSourceMode == InstallerSourceMode.StaticLocal)
        {
            if (!TryResolveLocalInstallerPathForAutoConfig(out var localPath)) return false;
            var ext = Path.GetExtension(localPath).ToLowerInvariant();
            return ext is ".msi" or ".exe" or ".zip";
        }

        if (_updateService is null) return false;
        if (string.IsNullOrWhiteSpace(GetAutoConfigProcessId())) return false;

        return InstallerSourceMode switch
        {
            InstallerSourceMode.StaticWeb => !string.IsNullOrWhiteSpace(DownloadUrl),
            InstallerSourceMode.DynamicWeb =>
                !string.IsNullOrWhiteSpace(DownloadBaseFolderUrl) &&
                (!string.IsNullOrWhiteSpace(DownloadSelectedFileName) || !string.IsNullOrWhiteSpace(DownloadSelectedVersion)),
            _ => false
        };
    }

    private async Task OpenAutoConfigAsync()
    {
        AutoConfigError = string.Empty;
        AutoConfigMsiProperties.Clear();
        AutoConfigSearchText = string.Empty;
        IsAutoConfigOpen = true;
        IsAutoConfigBusy = true;
        AutoConfigStatusText = "Preparazione...";

        try
        {
            var localPath = await EnsureInstallerCachedForAutoConfigAsync();
            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            {
                AutoConfigError = PortalAccessEnabled ? "Autenticazione richiesta per scaricare l'installer selezionato." : "Seleziona un file MSI/EXE locale per caricare le proprietà.";
                return;
            }

            var ext = Path.GetExtension(localPath).ToLowerInvariant();
            string msiPath;
            if (ext == ".msi")
            {
                msiPath = localPath;
            }
            else
            {
                if (_processExecutionService is null)
                {
                    AutoConfigError = "Auto-configurazione non disponibile: servizio di esecuzione processi mancante.";
                    return;
                }

                var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
                if (string.IsNullOrWhiteSpace(storageDir))
                    storageDir = Path.Combine(AppContext.BaseDirectory, "Data");
                var tempRoot = Path.Combine(storageDir, "temp", "AutoConfig", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                var extractedMsiPath = string.Empty;
                try
                {
                    var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync();
                    AutoConfigStatusText = "Estraggo i file...";
                    var extractArgs = $"x \"{localPath}\" -o\"{tempRoot}\" -y -ir!*.msi";
                    var extractResult = await _processExecutionService.RunAsync(sevenZipExe, extractArgs, runAsAdmin: false);
                    if (extractResult.ExitCode != 0)
                    {
                        AutoConfigError = $"Estrazione MSI fallita (exit {extractResult.ExitCode}).";
                        return;
                    }

                    extractedMsiPath = Directory.EnumerateFiles(tempRoot, "SetupRetail.msi", SearchOption.AllDirectories).FirstOrDefault()
                                      ?? Directory.EnumerateFiles(tempRoot, "*.msi", SearchOption.AllDirectories).FirstOrDefault()
                                      ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(extractedMsiPath))
                    {
                        AutoConfigError = "Nessun MSI trovato dentro l'EXE.";
                        return;
                    }

                    AutoConfigStatusText = "Carico le proprietà...";
                    msiPath = extractedMsiPath;
                    var propsInner = await Task.Run(() => ReadMsiPropertyTable(msiPath));
                    foreach (var (name, value) in propsInner
                                 .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                                 .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                                 .Take(200))
                    {
                        AutoConfigMsiProperties.Add(new MsiPropertyItem(name, value ?? string.Empty));
                    }

                    ApplyAutoConfigCommand.NotifyCanExecuteChanged();
                    OnPropertyChanged(nameof(FilteredAutoConfigMsiProperties));
                    OnPropertyChanged(nameof(EnabledFilteredAutoConfigMsiProperties));
                    OnPropertyChanged(nameof(DisabledFilteredAutoConfigMsiProperties));
                    OnPropertyChanged(nameof(HasEnabledAutoConfigMsiProperties));
                    OnPropertyChanged(nameof(HasDisabledAutoConfigMsiProperties));
                    OnPropertyChanged(nameof(HasEnabledAndDisabledAutoConfigMsiProperties));
                    AutoConfigStatusText = string.Empty;
                    return;
                }
                finally
                {
                    try { Directory.Delete(tempRoot, recursive: true); } catch { }
                }
            }

            AutoConfigStatusText = "Carico le proprietà...";
            var props = await Task.Run(() => ReadMsiPropertyTable(msiPath));
            foreach (var (name, value) in props
                .Where(p => !string.IsNullOrWhiteSpace(p.Key))
                .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Take(200))
            {
                AutoConfigMsiProperties.Add(new MsiPropertyItem(name, value ?? string.Empty));
            }

            ApplyAutoConfigCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(EnabledFilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(DisabledFilteredAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasEnabledAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasDisabledAutoConfigMsiProperties));
            OnPropertyChanged(nameof(HasEnabledAndDisabledAutoConfigMsiProperties));
            AutoConfigStatusText = string.Empty;
        }
        catch (Exception ex)
        {
            AutoConfigError = ex.Message;
        }
        finally
        {
            IsAutoConfigBusy = false;
        }
    }

    private bool TryResolveLocalInstallerPathForAutoConfig(out string path)
    {
        path = string.Empty;

        if (!IsInstallerMode)
            return false;

        var candidate = (FilePath ?? string.Empty).Trim();
        if (InstallerSourceMode == InstallerSourceMode.StaticLocal)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            if (!Path.IsPathRooted(candidate))
                candidate = Path.Combine(AppContext.BaseDirectory, candidate);

            if (!File.Exists(candidate))
                return false;

            path = candidate;
            return true;
        }

        var rel = (_editingProcessRelativePath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rel))
            return false;

        var abs = Path.IsPathRooted(rel) ? rel : Path.Combine(AppContext.BaseDirectory, rel);
        if (!File.Exists(abs))
            return false;

        path = abs;
        return true;
    }

    private bool CanAutoGenerateInstallerFlow()
    {
        if (!IsInstallerMode) return false;
        if (_processExecutionService is null) return false;
        if (!TryResolveLocalInstallerPathForAutoConfig(out var path)) return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".msi" or ".exe";
    }

    private async Task AutoGenerateInstallerFlowAsync()
    {
        ValidationError = null;

        if (!TryResolveLocalInstallerPathForAutoConfig(out var localPath))
            return;

        var ext = Path.GetExtension(localPath).ToLowerInvariant();

        if (ext == ".msi")
        {
            SelectedExeInstallerMode = ExeInstallerMode.Manual;
            return;
        }

        if (_processExecutionService is null)
            return;

        try
        {
            if (string.Equals(ExeInstallerAnalysis.TryDetectExeInstallerFamily(localPath), "WiX Burn", StringComparison.OrdinalIgnoreCase))
            {
                SelectedExeInstallerMode = ExeInstallerMode.AutoExtractMainMsi;
                return;
            }

            var sevenZipExe = await _processExecutionService.Ensure7ZipInstalledAsync();
            var listArgs = $"l -slt \"{localPath}\"";
            var listResult = await _processExecutionService.RunAsync(sevenZipExe, listArgs, runAsAdmin: false);

            if (listResult.ExitCode != 0)
            {
                SelectedExeInstallerMode = ExeInstallerMode.Auto;
                return;
            }

            var msiCount = ExeInstallerAnalysis.CountMsiPathsFrom7ZipSlt(listResult.StdOut);
            SelectedExeInstallerMode = msiCount switch
            {
                > 1 => ExeInstallerMode.AutoExtractAllMsis,
                1 => ExeInstallerMode.AutoExtractMainMsi,
                _ => ExeInstallerMode.Auto
            };
        }
        catch (Exception ex)
        {
            ValidationError = $"Auto-genera non disponibile: {ex.Message}";
        }
    }

    private string GetAutoConfigProcessId()
    {
        if (!string.IsNullOrWhiteSpace(_existingProcessId))
            return _existingProcessId!;

        if (string.IsNullOrWhiteSpace(_autoConfigTempProcessId))
            _autoConfigTempProcessId = Guid.NewGuid().ToString("N");

        return _autoConfigTempProcessId;
    }

    private DeploymentProcess BuildSnapshotProcessForAutoConfig()
    {
        var processId = GetAutoConfigProcessId();

        var process = new DeploymentProcess
        {
            Id = processId,
            Name = string.IsNullOrWhiteSpace(ProcessName) ? "Process" : ProcessName.Trim(),
            Kind = ProcessKind.Installer,
            InstallerSourceMode = InstallerSourceMode,
            RunAsAdmin = RunAsAdmin,
            RequiresInternet = RequiresInternet,
            RequiresAuth = PortalAccessEnabled,
            PortalId = PortalAccessEnabled ? SelectedPortalId : null,
        };

        if (InstallerSourceMode == InstallerSourceMode.StaticLocal)
        {
            process.RelativePath = FilePath;
            return process;
        }

        var fileName = TryGetFileNameFromUrl(DownloadUrl);
        if (InstallerSourceMode == InstallerSourceMode.DynamicWeb)
            fileName = DownloadSelectedFileName;

        var fallbackName = string.IsNullOrWhiteSpace(fileName) ? "installer.exe" : fileName;
        process.RelativePath = BuildDefaultInstallerCacheRelativePath(process.Id, fallbackName);

        process.DownloadUrl = InstallerSourceMode == InstallerSourceMode.StaticWeb ? DownloadUrl.Trim() : string.Empty;
        process.DownloadBaseFolderUrl = InstallerSourceMode == InstallerSourceMode.DynamicWeb ? DownloadBaseFolderUrl.Trim() : string.Empty;

        if (InstallerSourceMode == InstallerSourceMode.DynamicWeb)
        {
            process.DownloadSelectedFileName = DownloadSelectedFileName.Trim();
            process.DownloadSelectedFileTemplate = BuildDownloadTemplate(process.DownloadSelectedFileName);
            process.DownloadPickLatestFolderByName = !string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl);

            var selectedVersion = NormalizeVersionFolderName(DownloadSelectedVersion?.Trim() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(selectedVersion))
            {
                process.DownloadUseLatestVersion = false;
                process.DownloadVersionFolderName = selectedVersion;
            }
            else
            {
                process.DownloadUseLatestVersion = IsInstallerAutoUpdateEnabled;
                process.DownloadVersionFolderName = process.DownloadUseLatestVersion ? string.Empty : NormalizeVersionFolderName((_persistedVersionFolderName ?? string.Empty).Trim());
            }
        }

        return process;
    }

    private async Task<string> EnsureInstallerCachedForAutoConfigAsync()
    {
        if (InstallerSourceMode == InstallerSourceMode.StaticLocal)
        {
            if (!TryResolveLocalInstallerPathForAutoConfig(out var localPath))
                return string.Empty;

            return localPath;
        }

        if (_updateService is null)
            return string.Empty;

        var snapshot = BuildSnapshotProcessForAutoConfig();
        var localPathAbs = Path.Combine(AppContext.BaseDirectory, snapshot.RelativePath);

        if (snapshot.RequiresAuth && _authService is not null)
        {
            var authUrl = snapshot.InstallerSourceMode == InstallerSourceMode.DynamicWeb
                ? snapshot.DownloadBaseFolderUrl
                : snapshot.DownloadUrl;

            if (!string.IsNullOrWhiteSpace(authUrl) && !_authService.IsAuthenticatedForUrl(authUrl))
            {
                AutoConfigStatusText = "Autenticazione richiesta...";
                if (_openLoginAsync is not null)
                    await _openLoginAsync();

                if (!_authService.IsAuthenticatedForUrl(authUrl))
                    return string.Empty;
            }
        }

        var displayName = Path.GetFileName(snapshot.RelativePath) ?? "installer";
        AutoConfigStatusText = File.Exists(localPathAbs) ? $"Verifico {displayName}..." : $"Scarico {displayName}...";
        await _updateService.UpdateSingleInstallerAsync(snapshot);

        if (File.Exists(localPathAbs))
            return localPathAbs;

        return string.Empty;
    }

    private void CloseAutoConfig()
    {
        IsAutoConfigOpen = false;
        AutoConfigError = string.Empty;
        AutoConfigStatusText = string.Empty;
        IsAutoConfigBusy = false;
        AutoConfigSearchText = string.Empty;
    }

    private bool CanApplyAutoConfig() => IsAutoConfigOpen && AutoConfigMsiProperties.Any(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Value));

    private void ApplyAutoConfig()
    {
        var parts = AutoConfigMsiProperties
            .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{CanonicalizeMsiPropertyName(p.Name)}={FormatMsiPropertyValue(p.Value)}")
            .ToList();

        Arguments = string.Join(" ", parts);
    }

    private bool CanImportAutoConfigProfile() => IsAutoConfigOpen && !IsAutoConfigBusy;

    private bool CanExportAutoConfigProfile() =>
        IsAutoConfigOpen && !IsAutoConfigBusy && AutoConfigMsiProperties.Count > 0;

    private void ImportAutoConfigProfile()
    {
        AutoConfigError = string.Empty;

        var dlg = new OpenFileDialog
        {
            Title = "Importa profilo configurazioni",
            Filter = "Auto-config KlevaDeploy (*.kac.json;*.json)|*.kac.json;*.json|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            ApplyAutoConfigProfileJson(json);
        }
        catch (Exception ex)
        {
            AutoConfigError = $"Import fallito: {ex.Message}";
        }
    }

    private void ExportAutoConfigProfile()
    {
        AutoConfigError = string.Empty;

        var safeName = string.Join("_", (ProcessName ?? "Process").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Process";

        var dlg = new SaveFileDialog
        {
            Title = "Esporta profilo configurazioni",
            Filter = "Auto-config KlevaDeploy (*.kac.json)|*.kac.json|JSON (*.json)|*.json",
            FileName = $"{safeName}.kac.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var dto = new AutoConfigProfileDto
            {
                SchemaVersion = 1,
                Name = string.IsNullOrWhiteSpace(ProcessName) ? "Auto-config" : ProcessName.Trim(),
                Format = "msiKeyValue",
                Properties = AutoConfigMsiProperties
                    .OrderBy(p => p.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new AutoConfigPropertyDto
                    {
                        Name = p.Name,
                        Value = p.Value,
                        Enabled = p.IsEnabled
                    })
                    .ToList()
            };

            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
        }
        catch (Exception ex)
        {
            AutoConfigError = $"Export fallito: {ex.Message}";
        }
    }

    private void ApplyAutoConfigProfileJson(string json)
    {
        var dto = JsonSerializer.Deserialize<AutoConfigProfileDto>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        });
        if (dto is null || dto.Properties is null)
            throw new InvalidOperationException("File profilo non valido.");

        if (string.Equals(dto.Format, "rawArguments", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(dto.Arguments))
                throw new InvalidOperationException("Profilo non valido: manca 'arguments'.");

            Arguments = Environment.ExpandEnvironmentVariables(dto.Arguments).Trim();
            SubProcesses.Clear();
            if (dto.SubProcesses is not null && dto.SubProcesses.Count > 0)
            {
                foreach (var sp in dto.SubProcesses)
                {
                    var p = new DeploymentProcess
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = string.IsNullOrWhiteSpace(sp.Name) ? "Sottoprocesso" : sp.Name.Trim(),
                        Kind = ProcessKind.Installer,
                        InstallerSourceMode = InstallerSourceMode.StaticLocal,
                        RelativePath = (sp.RelativePath ?? string.Empty).Trim(),
                        Arguments = Environment.ExpandEnvironmentVariables(sp.Arguments ?? string.Empty).Trim(),
                        RunAsAdmin = sp.RunAsAdmin ?? false,
                        RequiresInternet = false,
                        IsRequired = false,
                        IsUserCreated = true,
                        EnabledByDefault = true
                    };
                    SubProcesses.Add(new SubProcessItem { Process = p });
                }
            }
            SelectedSubProcess = SubProcesses.FirstOrDefault();
            if (SubProcesses.Any(s => s.RunAsAdminEffective == true))
                RunAsAdmin = true;
            AutoConfigStatusText = "Argomenti importati.";
            AutoConfigError = string.Empty;
            IsAutoConfigOpen = false;
            return;
        }

        foreach (var it in AutoConfigMsiProperties)
            it.IsEnabled = false;

        foreach (var p in dto.Properties.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
        {
            var name = CanonicalizeMsiPropertyName(p.Name).Trim();
            if (name.Length == 0) continue;

            var existing = AutoConfigMsiProperties.FirstOrDefault(x =>
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new MsiPropertyItem(name, p.Value ?? string.Empty);
                AutoConfigMsiProperties.Add(existing);
            }

            existing.Value = p.Value ?? string.Empty;
            existing.IsEnabled = p.Enabled;
        }

        ApplyAutoConfigCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FilteredAutoConfigMsiProperties));
        OnPropertyChanged(nameof(EnabledFilteredAutoConfigMsiProperties));
        OnPropertyChanged(nameof(DisabledFilteredAutoConfigMsiProperties));
        OnPropertyChanged(nameof(HasEnabledAutoConfigMsiProperties));
        OnPropertyChanged(nameof(HasDisabledAutoConfigMsiProperties));
        OnPropertyChanged(nameof(HasEnabledAndDisabledAutoConfigMsiProperties));
    }

    private static readonly IReadOnlyDictionary<string, string> KnownMsiPropertyCaseMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["INSTALLAZIONEAUTOMATICA"] = "InstallazioneAutomatica",
            ["REINSTALLMODE"] = "ReinstallMode",
            ["IPSERVERDATABASE"] = "IpServerDatabase",
            ["PORTASERVER"] = "PortaServer",
            ["NOMEDATABASE"] = "NomeDatabase",
            ["PASSWORDDATABASE"] = "PasswordDatabase",
            ["LOGFILE"] = "LogFile"
        };

    private static string CanonicalizeMsiPropertyName(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) return string.Empty;
        return KnownMsiPropertyCaseMap.TryGetValue(n, out var mapped) ? mapped : n;
    }

    private sealed class AutoConfigProfileDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("format")]
        public string? Format { get; set; }
        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
        [JsonPropertyName("subProcesses")]
        public List<AutoConfigSubProcessDto>? SubProcesses { get; set; }
        [JsonPropertyName("properties")]
        public List<AutoConfigPropertyDto> Properties { get; set; } = new();
    }

    private sealed class AutoConfigSubProcessDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("relativePath")]
        public string? RelativePath { get; set; }
        [JsonPropertyName("arguments")]
        public string? Arguments { get; set; }
        [JsonPropertyName("runAsAdmin")]
        public bool? RunAsAdmin { get; set; }
    }

    private sealed class AutoConfigPropertyDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("value")]
        public string? Value { get; set; }
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }
    }

    public sealed class SubProcessItem : ObservableObject
    {
        public DeploymentSubProcess SubProcess { get; }

        public SubProcessItem() : this(new DeploymentSubProcess())
        {
        }

        public SubProcessItem(DeploymentSubProcess subProcess)
        {
            SubProcess = subProcess ?? new DeploymentSubProcess();
        }

        public DeploymentProcess? Process
        {
            get => SubProcess.Process;
            set
            {
                if (ReferenceEquals(SubProcess.Process, value)) return;
                SubProcess.Process = value;
                OnPropertyChanged(nameof(Process));
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(RelativePath));
                OnPropertyChanged(nameof(Arguments));
                OnPropertyChanged(nameof(RunAsAdminEffective));
                OnPropertyChanged(nameof(DisplayIcon));
                OnPropertyChanged(nameof(HasCustomIcon));
                OnPropertyChanged(nameof(CustomIconLightPath));
                OnPropertyChanged(nameof(CustomIconDarkPath));
            }
        }

        public string Name
        {
            get
            {
                var n = SubProcess.Process?.Name;
                if (!string.IsNullOrWhiteSpace(n)) return n;
                return SubProcess.Name ?? string.Empty;
            }
            set
            {
                var v = value ?? string.Empty;
                if (SubProcess.Process is not null)
                {
                    if (string.Equals(SubProcess.Process.Name, v, StringComparison.Ordinal)) return;
                    SubProcess.Process.Name = v;
                }
                else
                {
                    if (string.Equals(SubProcess.Name, v, StringComparison.Ordinal)) return;
                    SubProcess.Name = v;
                }
                OnPropertyChanged();
            }
        }

        public string KindLabel =>
            SubProcess.Process?.Kind.ToString() ??
            InferKindFromPath(SubProcess.RelativePath);

        public string RelativePath
        {
            get
            {
                var p = SubProcess.Process?.RelativePath;
                if (!string.IsNullOrWhiteSpace(p)) return p;
                return SubProcess.RelativePath ?? string.Empty;
            }
            set
            {
                var v = value ?? string.Empty;
                if (SubProcess.Process is not null)
                {
                    if (string.Equals(SubProcess.Process.RelativePath, v, StringComparison.Ordinal)) return;
                    SubProcess.Process.RelativePath = v;
                }
                else
                {
                    if (string.Equals(SubProcess.RelativePath, v, StringComparison.Ordinal)) return;
                    SubProcess.RelativePath = v;
                }
                OnPropertyChanged();
            }
        }

        public string Arguments
        {
            get
            {
                var a = SubProcess.Process?.Arguments;
                if (!string.IsNullOrWhiteSpace(a)) return a;
                return SubProcess.Arguments ?? string.Empty;
            }
            set
            {
                var v = value ?? string.Empty;
                if (SubProcess.Process is not null)
                {
                    if (string.Equals(SubProcess.Process.Arguments, v, StringComparison.Ordinal)) return;
                    SubProcess.Process.Arguments = v;
                }
                else
                {
                    if (string.Equals(SubProcess.Arguments, v, StringComparison.Ordinal)) return;
                    SubProcess.Arguments = v;
                }
                OnPropertyChanged();
            }
        }

        public bool? RunAsAdminEffective => SubProcess.RunAsAdmin ?? SubProcess.Process?.RunAsAdmin;

        public string DisplayIcon => SubProcess.Process?.Icon ?? "📦";

        public bool HasCustomIcon =>
            !string.IsNullOrWhiteSpace(SubProcess.Process?.CustomIconLightPath) ||
            !string.IsNullOrWhiteSpace(SubProcess.Process?.CustomIconDarkPath);

        public string? CustomIconLightPath => SubProcess.Process?.CustomIconLightPath;

        public string? CustomIconDarkPath => SubProcess.Process?.CustomIconDarkPath;
    }

    private static string InferKindFromPath(string? relativePath)
    {
        var ext = Path.GetExtension(relativePath ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".ps1" => ProcessKind.PowerShellScript.ToString(),
            ".bat" => ProcessKind.BatchScript.ToString(),
            ".cmd" => ProcessKind.BatchScript.ToString(),
            ".sh" => ProcessKind.BashScript.ToString(),
            ".reg" => ProcessKind.RegistryFile.ToString(),
            _ => ProcessKind.Installer.ToString()
        };
    }

    private static DeploymentProcess? BuildLegacySubProcess(DeploymentSubProcess sp)
    {
        var rel = (sp.RelativePath ?? string.Empty).Trim();
        var args = (sp.Arguments ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rel) && string.IsNullOrWhiteSpace(args)) return null;

        var kind = InferKindFromPath(rel) switch
        {
            nameof(ProcessKind.PowerShellScript) => ProcessKind.PowerShellScript,
            nameof(ProcessKind.BatchScript) => ProcessKind.BatchScript,
            nameof(ProcessKind.BashScript) => ProcessKind.BashScript,
            nameof(ProcessKind.RegistryFile) => ProcessKind.RegistryFile,
            _ => ProcessKind.Installer
        };

        return new DeploymentProcess
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(sp.Name) ? "Sottoprocesso" : sp.Name.Trim(),
            Kind = kind,
            InstallerSourceMode = InstallerSourceMode.StaticLocal,
            RelativePath = rel,
            Arguments = args,
            RunAsAdmin = sp.RunAsAdmin ?? false,
            RequiresInternet = false,
            IsRequired = false,
            IsUserCreated = true,
            EnabledByDefault = true
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
        Icon = p.HasCustomIcon ? p.Icon : DeploymentProcess.ResolveBuiltInIcon(p.IconKey, p.Icon),
        CustomIconLightPath = p.CustomIconLightPath,
        CustomIconDarkPath = p.CustomIconDarkPath,
        IsUserCreated = p.IsUserCreated,
        SubProcesses = p.SubProcesses?.ToList() ?? new()
    };

    private DeploymentProcess BuildEditorProcessSnapshot(bool includeSubProcesses)
    {
        return new DeploymentProcess
        {
            Id = _existingProcessId ?? Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(ProcessName) ? "Current Process" : ProcessName.Trim(),
            Description = Description.Trim(),
            Kind = SelectedProcessKind,
            RelativePath = FilePath.Trim(),
            Arguments = Arguments.Trim(),
            RunAsAdmin = RunAsAdmin,
            RequiresInternet = RequiresInternet,
            ScriptContent = ScriptContent,
            InstallDirectory = InstallDirectory.Trim(),
            IconKey = SelectedIconKey,
            Icon = Icon,
            CustomIconLightPath = CustomIconLightPath,
            CustomIconDarkPath = CustomIconDarkPath,
            IsUserCreated = true,
            EnabledByDefault = true,
            SubProcesses = includeSubProcesses
                ? BuildEditorSubProcesses()
                : new List<DeploymentSubProcess>()
        };
    }

    private List<DeploymentSubProcess> BuildEditorSubProcesses()
    {
        return SubProcesses
            .Where(s =>
                s.Process is not null ||
                !string.IsNullOrWhiteSpace(s.SubProcess.Name) ||
                !string.IsNullOrWhiteSpace(s.SubProcess.RelativePath) ||
                !string.IsNullOrWhiteSpace(s.SubProcess.Arguments))
            .Select(s => new DeploymentSubProcess
            {
                Name = (s.SubProcess.Name ?? string.Empty).Trim(),
                Process = s.Process is null ? null : CloneProcess(s.Process),
                RelativePath = (s.SubProcess.RelativePath ?? string.Empty).Trim(),
                Arguments = (s.SubProcess.Arguments ?? string.Empty).Trim(),
                RunAsAdmin = s.SubProcess.RunAsAdmin
            })
            .ToList();
    }

    private bool CanRemoveSubProcess(SubProcessItem? item) => item is not null && SubProcesses.Contains(item);

    private void RemoveSubProcess(SubProcessItem? item)
    {
        if (item is null) return;
        var idx = SubProcesses.IndexOf(item);
        if (idx < 0) return;
        SubProcesses.RemoveAt(idx);
        if (ReferenceEquals(SelectedSubProcess, item))
            SelectedSubProcess = idx < SubProcesses.Count ? SubProcesses[idx] : SubProcesses.LastOrDefault();
        MoveSubProcessUpCommand.NotifyCanExecuteChanged();
        MoveSubProcessDownCommand.NotifyCanExecuteChanged();
        RemoveSubProcessCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveSubProcessUp(SubProcessItem? item)
    {
        if (item is null) return false;
        var idx = SubProcesses.IndexOf(item);
        return idx > 0;
    }

    private void MoveSubProcessUp(SubProcessItem? item)
    {
        if (item is null) return;
        var idx = SubProcesses.IndexOf(item);
        if (idx <= 0) return;
        SubProcesses.Move(idx, idx - 1);
        SelectedSubProcess = item;
        MoveSubProcessUpCommand.NotifyCanExecuteChanged();
        MoveSubProcessDownCommand.NotifyCanExecuteChanged();
    }

    private bool CanMoveSubProcessDown(SubProcessItem? item)
    {
        if (item is null) return false;
        var idx = SubProcesses.IndexOf(item);
        return idx >= 0 && idx < SubProcesses.Count - 1;
    }

    private void MoveSubProcessDown(SubProcessItem? item)
    {
        if (item is null) return;
        var idx = SubProcesses.IndexOf(item);
        if (idx < 0 || idx >= SubProcesses.Count - 1) return;
        SubProcesses.Move(idx, idx + 1);
        SelectedSubProcess = item;
        MoveSubProcessUpCommand.NotifyCanExecuteChanged();
        MoveSubProcessDownCommand.NotifyCanExecuteChanged();
    }

    private sealed class ProcessBundleDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("process")]
        public DeploymentProcess? Process { get; set; }
    }

    private void ImportProcess()
    {
        ValidationError = null;
        var dlg = new OpenFileDialog
        {
            Title = "Importa processo",
            Filter = "Processo KlevaDeploy (*.kdp.json;*.json)|*.kdp.json;*.json|Tutti i file (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var json = File.ReadAllText(dlg.FileName);
            static JsonDocument ParseDocument(string content)
            {
                return JsonDocument.Parse(content, new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            }

            using var doc = ParseDocument(json);
            if (!doc.RootElement.TryGetProperty("process", out _))
            {
                if (doc.RootElement.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String)
                {
                    var fmtValue = fmt.GetString() ?? string.Empty;
                    throw new InvalidOperationException($"File non valido per l'import processo: sembra un profilo legacy (*.{fmtValue} / .kac.json). Seleziona un file *.kdp.json.");
                }

                throw new InvalidOperationException("File processo non valido. Seleziona un file *.kdp.json.");
            }

            var dto = JsonSerializer.Deserialize<ProcessBundleDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });
            if (dto?.Process is null)
                throw new InvalidOperationException("File processo non valido.");

            var imported = dto.Process;
            imported.NormalizeIconRecursively();
            InitializeForEdit(imported);
        }
        catch (Exception ex)
        {
            ValidationError = $"Import fallito: {ex.Message}";
        }
    }

    private void ExportProcess()
    {
        ValidationError = null;

        var safeName = string.Join("_", (ProcessName ?? "Process").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Process";

        var dlg = new SaveFileDialog
        {
            Title = "Esporta processo",
            Filter = "Processo KlevaDeploy (*.kdp.json)|*.kdp.json|JSON (*.json)|*.json",
            FileName = $"{safeName}.kdp.json",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var process = new DeploymentProcess
            {
                Id = _existingProcessId ?? Guid.NewGuid().ToString("N"),
                Name = (ProcessName ?? string.Empty).Trim(),
                Description = (Description ?? string.Empty).Trim(),
                Kind = SelectedProcessKind,
                Arguments = (Arguments ?? string.Empty).Trim(),
                RunAsAdmin = RunAsAdmin,
                RequiresInternet = RequiresInternet,
                RequiresAuth = PortalAccessEnabled,
                PortalId = PortalAccessEnabled ? SelectedPortalId : null,
                ScriptContent = (ScriptContent ?? string.Empty).Trim(),
                InstallDirectory = (InstallDirectory ?? string.Empty).Trim(),
                InstallerSourceMode = InstallerSourceMode,
                RelativePath = InstallerSourceMode == InstallerSourceMode.StaticLocal ? (FilePath ?? string.Empty).Trim() : string.Empty,
                DownloadUrl = InstallerSourceMode == InstallerSourceMode.StaticWeb ? (DownloadUrl ?? string.Empty).Trim() : string.Empty,
                DownloadBaseFolderUrl = InstallerSourceMode == InstallerSourceMode.DynamicWeb ? (DownloadBaseFolderUrl ?? string.Empty).Trim() : string.Empty,
                DownloadSelectedFileName = InstallerSourceMode == InstallerSourceMode.DynamicWeb ? (DownloadSelectedFileName ?? string.Empty).Trim() : string.Empty,
                DownloadSelectedFileTemplate = string.Empty,
                DownloadVersionFolderName = NormalizeVersionFolderName(DownloadSelectedVersion?.Trim() ?? string.Empty),
                DownloadUseLatestVersion = IsInstallerAutoUpdateEnabled,
                SubProcesses = SubProcesses
                    .Select(s => new DeploymentSubProcess
                    {
                        Name = (s.SubProcess.Name ?? string.Empty).Trim(),
                        Process = s.Process is null ? null : CloneProcess(s.Process),
                        RelativePath = (s.SubProcess.RelativePath ?? string.Empty).Trim(),
                        Arguments = (s.SubProcess.Arguments ?? string.Empty).Trim(),
                        RunAsAdmin = s.SubProcess.RunAsAdmin
                    })
                    .ToList()
            };

            process = MaterializeExternalScriptsForExport(process);

            var dto = new ProcessBundleDto { SchemaVersion = 1, Process = process };
            var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
        }
        catch (Exception ex)
        {
            ValidationError = $"Export fallito: {ex.Message}";
        }
    }

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
            .Select(sp =>
            {
                var clonedSub = new DeploymentSubProcess
                {
                    Name = sp.Name,
                    RelativePath = sp.RelativePath,
                    Arguments = sp.Arguments,
                    RunAsAdmin = sp.RunAsAdmin,
                    Process = sp.Process is null ? null : MaterializeExternalScriptsForExport(sp.Process)
                };
                return clonedSub;
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

    private static string FormatMsiPropertyValue(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return "\"\"";
        var needsQuotes = v.Any(char.IsWhiteSpace) || v.Contains(';') || v.Contains('=');
        v = v.Replace("\"", "\\\"");
        return needsQuotes ? $"\"{v}\"" : v;
    }

    private static IReadOnlyDictionary<string, string?> ReadMsiPropertyTable(string msiPath)
    {
        var type = Type.GetTypeFromProgID("WindowsInstaller.Installer");
        if (type is null)
            throw new InvalidOperationException("Windows Installer COM is not available.");

        object? installer = null;
        object? database = null;
        object? view = null;
        try
        {
            installer = Activator.CreateInstance(type);
            if (installer is null)
                throw new InvalidOperationException("Failed to create WindowsInstaller.Installer COM object.");

            database = type.InvokeMember("OpenDatabase", System.Reflection.BindingFlags.InvokeMethod, null, installer, new object[] { msiPath, 0 });
            if (database is null)
                throw new InvalidOperationException("Failed to open MSI database.");

            var dbType = database.GetType();
            view = dbType.InvokeMember("OpenView", System.Reflection.BindingFlags.InvokeMethod, null, database, new object[] { "SELECT `Property`,`Value` FROM `Property`" });
            if (view is null)
                throw new InvalidOperationException("Failed to open MSI view.");

            var viewType = view.GetType();
            viewType.InvokeMember("Execute", System.Reflection.BindingFlags.InvokeMethod, null, view, null);

            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var record = viewType.InvokeMember("Fetch", System.Reflection.BindingFlags.InvokeMethod, null, view, null);
                if (record is null) break;

                var recordType = record.GetType();
                var name = recordType.InvokeMember("StringData", System.Reflection.BindingFlags.GetProperty, null, record, new object[] { 1 }) as string;
                var val = recordType.InvokeMember("StringData", System.Reflection.BindingFlags.GetProperty, null, record, new object[] { 2 }) as string;

                if (!string.IsNullOrWhiteSpace(name))
                    dict[name] = val;

                try { Marshal.FinalReleaseComObject(record); } catch { }
            }

            try { viewType.InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, view, null); } catch { }
            return dict;
        }
        finally
        {
            if (view is not null) { try { Marshal.FinalReleaseComObject(view); } catch { } }
            if (database is not null) { try { Marshal.FinalReleaseComObject(database); } catch { } }
            if (installer is not null) { try { Marshal.FinalReleaseComObject(installer); } catch { } }
        }
    }

    public sealed class MsiPropertyItem : ObservableObject
    {
        public string Name { get; }
        public string DefaultValue { get; }

        private string _value;
        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public MsiPropertyItem(string name, string defaultValue)
        {
            Name = name;
            DefaultValue = defaultValue;
            _value = defaultValue;
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
            InstallDirectory = InstallDirectory.Trim(),
            IconKey = SelectedIconKey,
            Icon = Icon,
            CustomIconLightPath = CustomIconLightPath,
            CustomIconDarkPath = CustomIconDarkPath,
            IsUserCreated = true,
            EnabledByDefault = true
        };
        process.SubProcesses = IsSubProcessEditor
            ? new List<DeploymentSubProcess>()
            : SubProcesses
            .Where(s =>
                s.Process is not null ||
                !string.IsNullOrWhiteSpace(s.SubProcess.Name) ||
                !string.IsNullOrWhiteSpace(s.SubProcess.RelativePath) ||
                !string.IsNullOrWhiteSpace(s.SubProcess.Arguments))
            .Select(s => new DeploymentSubProcess
            {
                Name = (s.SubProcess.Name ?? string.Empty).Trim(),
                Process = s.Process is null ? null : CloneProcess(s.Process),
                RelativePath = (s.SubProcess.RelativePath ?? string.Empty).Trim(),
                Arguments = (s.SubProcess.Arguments ?? string.Empty).Trim(),
                RunAsAdmin = s.SubProcess.RunAsAdmin
            })
            .ToList();

        process.RequiresAuth = PortalAccessEnabled;
        process.PortalId = PortalAccessEnabled ? SelectedPortalId : null;

        if (IsInstallerMode)
        {
            process.InstallerSourceMode = InstallerSourceMode;
            process.RelativePath = InstallerSourceMode switch
            {
                InstallerSourceMode.StaticLocal => FilePath,
                InstallerSourceMode.StaticWeb => BuildDefaultInstallerCacheRelativePath(process.Id, TryGetFileNameFromUrl(DownloadUrl)),
                InstallerSourceMode.DynamicWeb => BuildDefaultInstallerCacheRelativePath(process.Id, DownloadSelectedFileName),
                _ => BuildDefaultInstallerCacheRelativePath(process.Id, "installer.exe")
            };

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

            process.DownloadUseLatestVersion = IsInstallerAutoUpdateEnabled;
            var selectedVersion = DownloadSelectedVersion?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selectedVersion))
                selectedVersion = (_persistedVersionFolderName ?? string.Empty).Trim();
            selectedVersion = NormalizeVersionFolderName(selectedVersion);
            if (string.IsNullOrWhiteSpace(selectedVersion) &&
                process.DownloadUseLatestVersion &&
                !string.IsNullOrWhiteSpace(_latestRemoteFolderName))
            {
                selectedVersion = _latestRemoteFolderName;
            }
            process.DownloadVersionFolderName = selectedVersion;
            _persistedVersionFolderName = selectedVersion;
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
        if (!IsSubProcessEditor && HasUnsavedSubProcessChanges)
        {
            IsSubProcessChangesPromptOpen = true;
            return;
        }

        CancelInternal();
    }

    private void CancelInternal()
    {
        IsSubProcessChangesPromptOpen = false;
        ValidationError = null;
        CreatedProcess = null;
        DialogResult = false;
    }

    private void DiscardSubProcessChangesAndClose()
    {
        CancelInternal();
    }

    private void SaveFromSubProcessChangesPrompt()
    {
        IsSubProcessChangesPromptOpen = false;
        Save();
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

        if (PortalAccessEnabled)
        {
            if (string.IsNullOrWhiteSpace(SelectedPortalId))
            {
                ValidationError = "Select a portal.";
                return false;
            }
            if (AvailablePortals.Count > 0 && !AvailablePortals.Any(p => string.Equals(p.Id, SelectedPortalId, StringComparison.OrdinalIgnoreCase)))
            {
                ValidationError = "Select a valid portal.";
                return false;
            }
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

                var normalizedUrl = NormalizeAbsoluteUrl(DownloadUrl);
                if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
                {
                    ValidationError = "Link non valido.";
                    return false;
                }

                if (!uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    ValidationError = "Il link deve puntare a un file .exe.";
                    return false;
                }

                DownloadUrl = normalizedUrl;
            }
            else if (InstallerSourceMode == InstallerSourceMode.DynamicWeb)
            {
                if (string.IsNullOrWhiteSpace(DownloadBaseFolderUrl))
                {
                    ValidationError = "Inserisci una cartella web (es. .../Aggiornamenti/Retail/).";
                    return false;
                }

                if (!IsRemoteFolderNavigationMode &&
                    !IsInstallerAutoUpdateEnabled &&
                    HasRemoteInstallerVersions &&
                    string.IsNullOrWhiteSpace(DownloadSelectedVersion))
                {
                    ValidationError = "Seleziona una versione.";
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
            ValidationError = "Download feature is not available.";
            return;
        }

        if (!_authService.IsAuthenticatedForUrl(DownloadBaseFolderUrl))
        {
            ValidationError = "Please login to this portal before loading installers.";
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

                var selectedVersion = (DownloadSelectedVersion ?? string.Empty).Trim();
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

                if (IsInstallerAutoUpdateEnabled &&
                    _forceLatestVersionOnRefresh &&
                    !string.IsNullOrWhiteSpace(_latestRemoteFolderName))
                {
                    selectedVersion = _latestRemoteFolderName;
                    _persistedVersionFolderName = _latestRemoteFolderName;
                }
                else
                {
                    var saved = (_loadedSavedVersionFolderName ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(saved))
                        selectedVersion = saved;
                    else
                    {
                        var persisted = (_persistedVersionFolderName ?? string.Empty).Trim();
                        if (!string.IsNullOrWhiteSpace(persisted))
                            selectedVersion = persisted;
                    }
                }

                if (string.IsNullOrWhiteSpace(selectedVersion) ||
                    !folders.Any(v => string.Equals(v, selectedVersion, StringComparison.OrdinalIgnoreCase)))
                {
                    var hadSaved = !string.IsNullOrWhiteSpace(selectedVersion);
                    selectedVersion = _latestRemoteFolderName;

                    if (hadSaved)
                    {
                        _log?.Warning($"Saved version '{_loadedSavedVersionFolderName}' not found in folder list for '{ProcessName}'. Falling back to latest '{_latestRemoteFolderName}'.");
                    }

                    _suppressVersionAutoLoad = true;
                    DownloadSelectedVersion = selectedVersion;
                    _suppressVersionAutoLoad = false;
                }
                else
                {
                    var exact = folders.FirstOrDefault(v => string.Equals(v, selectedVersion, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(exact) && !string.Equals(exact, DownloadSelectedVersion, StringComparison.Ordinal))
                    {
                        _suppressVersionAutoLoad = true;
                        DownloadSelectedVersion = exact;
                        _suppressVersionAutoLoad = false;
                    }
                    selectedVersion = exact ?? selectedVersion;
                }

                if (!string.IsNullOrWhiteSpace(selectedVersion) && !IsRemoteFolderNavigationMode)
                    _persistedVersionFolderName = selectedVersion;

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

            var desiredFile = (DownloadSelectedFileName ?? string.Empty).Trim();
            if (desiredFile.Contains("{VERSION}", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(DownloadSelectedVersion))
            {
                desiredFile = desiredFile.Replace("{VERSION}", DownloadSelectedVersion.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(desiredFile) && !string.IsNullOrWhiteSpace(_existingDownloadSelectedFileTemplate))
            {
                var candidate = _existingDownloadSelectedFileTemplate.Trim();
                if (candidate.Contains("{VERSION}", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(DownloadSelectedVersion))
                {
                    candidate = candidate.Replace("{VERSION}", DownloadSelectedVersion.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                desiredFile = candidate;
            }

            if (!string.IsNullOrWhiteSpace(desiredFile) &&
                RemoteInstallerFiles.Any(f => string.Equals(f, desiredFile, StringComparison.OrdinalIgnoreCase)))
            {
                DownloadSelectedFileName = desiredFile;
            }
            else if (!string.IsNullOrWhiteSpace(DownloadSelectedFileName) &&
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
        finally
        {
            _forceLatestVersionOnRefresh = false;
        }
    }

    private async Task OnDownloadSelectedVersionChangedAsync()
    {
        if (_downloadDirectoryListingService is null) return;
        if (_authService is null || !_authService.IsAuthenticatedForUrl(DownloadBaseFolderUrl)) return;
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
            var desiredFile = (DownloadSelectedFileName ?? string.Empty).Trim();
            if (desiredFile.Contains("{VERSION}", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(DownloadSelectedVersion))
            {
                desiredFile = desiredFile.Replace("{VERSION}", DownloadSelectedVersion.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            if (string.IsNullOrWhiteSpace(desiredFile) && !string.IsNullOrWhiteSpace(_existingDownloadSelectedFileTemplate))
            {
                var candidate = _existingDownloadSelectedFileTemplate.Trim();
                if (candidate.Contains("{VERSION}", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(DownloadSelectedVersion))
                {
                    candidate = candidate.Replace("{VERSION}", DownloadSelectedVersion.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                desiredFile = candidate;
            }

            if (!string.IsNullOrWhiteSpace(desiredFile) &&
                RemoteInstallerFiles.Any(f => string.Equals(f, desiredFile, StringComparison.OrdinalIgnoreCase)))
            {
                DownloadSelectedFileName = desiredFile;
            }
            else if (!string.IsNullOrWhiteSpace(DownloadSelectedFileName) &&
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
        if (!Uri.TryCreate(NormalizeAbsoluteUrl(url), UriKind.Absolute, out var uri)) return null;

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
        var versionToken = (DownloadSelectedVersion ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(versionToken))
            versionToken = _latestRemoteFolderName;
        if (string.IsNullOrWhiteSpace(versionToken)) return selectedFileName;

        return selectedFileName.Contains(versionToken, StringComparison.OrdinalIgnoreCase)
            ? selectedFileName.Replace(versionToken, "{VERSION}", StringComparison.OrdinalIgnoreCase)
            : selectedFileName;
    }

    private static InstallerSourceMode InferInstallerSourceMode(DeploymentProcess process)
    {
        if (process.Kind != ProcessKind.Installer) return InstallerSourceMode.StaticLocal;
        if (!string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl)) return InstallerSourceMode.DynamicWeb;
        if (!string.IsNullOrWhiteSpace(process.DownloadUrl)) return InstallerSourceMode.StaticWeb;
        return process.InstallerSourceMode;
    }

    private static string BuildDefaultInstallerCacheRelativePath(string processId, string? fileName)
    {
        var safe = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
            safe = "installer.exe";

        return Path.Combine("Data", "installers", processId, safe);
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim();
    }

    private static string TryGetFileNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (!Uri.TryCreate(NormalizeAbsoluteUrl(url), UriKind.Absolute, out var uri)) return string.Empty;
        var name = Path.GetFileName(uri.AbsolutePath);
        return name ?? string.Empty;
    }

    private static string NormalizeAbsoluteUrl(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        if (Uri.TryCreate(raw, UriKind.Absolute, out _)) return raw;
        if (!raw.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate("https://" + raw, UriKind.Absolute, out _))
        {
            return "https://" + raw;
        }

        return raw;
    }

    private static string NormalizeVersionFolderName(string? value)
    {
        var v = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;

        v = v.TrimEnd('/', '\\');
        var slash = v.LastIndexOf('/');
        var backslash = v.LastIndexOf('\\');
        var idx = Math.Max(slash, backslash);
        if (idx >= 0 && idx + 1 < v.Length)
            v = v[(idx + 1)..];

        return v.Trim();
    }

    private void LoadPortalsFromPreferences()
    {
        AvailablePortals.Clear();
        var portals = _prefsService?.Preferences.Portals;
        if (portals is null) return;

        foreach (var p in portals.Where(p => p is not null))
        {
            AvailablePortals.Add(new PortalOption(p!.Id, p!.Name));
        }

        if (string.IsNullOrWhiteSpace(SelectedPortalId))
            SelectedPortalId = _prefsService?.Preferences.SelectedPortalId;
    }

    private string? GuessPortalIdFromProcess(DeploymentProcess process)
    {
        var prefs = _prefsService?.Preferences;
        if (prefs?.Portals is null || prefs.Portals.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(process.PortalId))
            return process.PortalId;

        var url = process.InstallerSourceMode == InstallerSourceMode.DynamicWeb
            ? process.DownloadBaseFolderUrl
            : process.DownloadUrl;

        if (Uri.TryCreate(NormalizeAbsoluteUrl(url), UriKind.Absolute, out var processUri))
        {
            foreach (var p in prefs.Portals.Where(p => p is not null))
            {
                if (Uri.TryCreate((p!.HomeUrl ?? string.Empty).Trim(), UriKind.Absolute, out var portalUri) &&
                    string.Equals(portalUri.Host, processUri.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return p.Id;
                }
            }
        }

        return prefs.SelectedPortalId;
    }

    private static bool RequiresAuthForUrl(string url)
    {
        if (!Uri.TryCreate(NormalizeAbsoluteUrl(url), UriKind.Absolute, out var u)) return false;
        return string.Equals(u.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase);
    }
}

public record IconOption(string Key, string DisplayName);
public record PortalOption(string Id, string Name);
