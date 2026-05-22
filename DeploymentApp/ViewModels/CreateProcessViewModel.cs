using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Models;
using Microsoft.Win32;

namespace DeploymentApp.ViewModels;

public sealed partial class CreateProcessViewModel : ObservableObject
{
    private string? _existingProcessId;

    [ObservableProperty] private string _processName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private ProcessKind _selectedProcessKind = ProcessKind.Installer;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _arguments = string.Empty;
    [ObservableProperty] private string _scriptContent = string.Empty;
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private bool _requiresInternet;
    [ObservableProperty] private string _selectedIconKey = "IconPackage";
    [ObservableProperty] private string? _validationError;
    [ObservableProperty] private string _title = "Nuovo Processo";
    [ObservableProperty] private bool _isEditMode;

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

    partial void OnSelectedProcessKindChanged(ProcessKind value)
    {
        OnPropertyChanged(nameof(IsInstallerMode));
        OnPropertyChanged(nameof(IsScriptMode));
        ValidationError = null;
    }

    [RelayCommand]
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

    [RelayCommand]
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

    [RelayCommand]
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
