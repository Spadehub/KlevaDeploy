using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using Microsoft.Win32;

namespace KlevaDeploy.ViewModels;

public sealed class CreateProcessViewModel : ObservableObject
{
    private string? _existingProcessId;

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

    public ObservableCollection<ProcessKind> AvailableProcessKinds { get; } = new()
    {
        ProcessKind.Installer,
        ProcessKind.PowerShellScript,
        ProcessKind.BatchScript
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
    public bool IsScriptMode => SelectedProcessKind == ProcessKind.PowerShellScript || SelectedProcessKind == ProcessKind.BatchScript;

    public DeploymentProcess? CreatedProcess { get; private set; }
    public bool DialogResult { get; private set; }

    public IRelayCommand BrowseFileCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public CreateProcessViewModel()
    {
        BrowseFileCommand = new RelayCommand(BrowseFile);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    public void InitializeForEdit(DeploymentProcess process)
    {
        _existingProcessId = process.Id;
        ProcessName = process.Name;
        Description = process.Description;
        SelectedProcessKind = process.Kind;
        FilePath = process.RelativePath;
        Arguments = process.Arguments;
        ScriptContent = process.ScriptContent;
        RunAsAdmin = process.RunAsAdmin;
        RequiresInternet = process.RequiresInternet;
        SelectedIconKey = process.IconKey;
        Title = "Modifica Processo";
        IsEditMode = true;
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
            process.RelativePath = FilePath;
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
        DialogResult = false;
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
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                ValidationError = "Il percorso del file è obbligatorio per gli installer.";
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
}

public record IconOption(string Key, string DisplayName);
