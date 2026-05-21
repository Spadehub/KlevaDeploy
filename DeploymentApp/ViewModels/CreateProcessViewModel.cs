using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Models;
using Microsoft.Win32;

namespace DeploymentApp.ViewModels;

public sealed partial class CreateProcessViewModel : ObservableObject
{
    [ObservableProperty] private string _processName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private ProcessKind _selectedProcessKind = ProcessKind.Installer;
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _arguments = string.Empty;
    [ObservableProperty] private string _scriptContent = string.Empty;
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private string _selectedIconKey = "IconPackage";
    [ObservableProperty] private string? _validationError;

    public ObservableCollection<ProcessKind> AvailableProcessKinds { get; } = new()
    {
        ProcessKind.Installer,
        ProcessKind.PowerShellScript,
        ProcessKind.BatchScript
    };

    public ObservableCollection<IconOption> AvailableIcons { get; } = new()
    {
        new("IconPackage", "📦 Package"),
        new("IconScript", "💻 Script"),
        new("IconSettings", "⚙️ Settings"),
        new("IconDownload", "⬇️ Download"),
        new("IconInstall", "▶️ Install"),
        new("IconSuccess", "✅ Success"),
        new("IconWarning", "⚠️ Warning"),
        new("IconError", "❌ Error"),
        new("IconRefresh", "🔄 Refresh"),
        new("IconLock", "🔒 Lock"),
        new("IconUnlock", "🔓 Unlock"),
        new("IconLog", "📋 Log")
    };

    public bool IsInstallerMode => SelectedProcessKind == ProcessKind.Installer;
    public bool IsScriptMode => SelectedProcessKind == ProcessKind.PowerShellScript || SelectedProcessKind == ProcessKind.BatchScript;

    public DeploymentProcess? CreatedProcess { get; private set; }
    public bool DialogResult { get; private set; }

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
            dialog.Filter = "Installer Files (*.exe;*.msi;*.zip)|*.exe;*.msi;*.zip|All Files (*.*)|*.*";
            dialog.Title = "Select Installer File";
        }
        else if (SelectedProcessKind == ProcessKind.PowerShellScript)
        {
            dialog.Filter = "PowerShell Scripts (*.ps1)|*.ps1|All Files (*.*)|*.*";
            dialog.Title = "Select PowerShell Script";
        }
        else if (SelectedProcessKind == ProcessKind.BatchScript)
        {
            dialog.Filter = "Batch Scripts (*.bat;*.cmd)|*.bat;*.cmd|All Files (*.*)|*.*";
            dialog.Title = "Select Batch Script";
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
            Id = Guid.NewGuid().ToString("N"),
            Name = ProcessName.Trim(),
            Description = Description.Trim(),
            Kind = SelectedProcessKind,
            Arguments = Arguments.Trim(),
            RunAsAdmin = RunAsAdmin,
            IsRequired = IsRequired,
            IconKey = SelectedIconKey,
            IsUserCreated = true,
            EnabledByDefault = true
        };

        if (IsInstallerMode)
        {
            // For installers, store relative path or full path
            process.RelativePath = FilePath;
        }
        else if (IsScriptMode)
        {
            // For scripts, use inline content if provided, otherwise file path
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
            ValidationError = "Process name is required.";
            return false;
        }

        if (IsInstallerMode)
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                ValidationError = "File path is required for installers.";
                return false;
            }

            if (!File.Exists(FilePath))
            {
                ValidationError = "The specified file does not exist.";
                return false;
            }

            var ext = Path.GetExtension(FilePath).ToLowerInvariant();
            if (ext != ".exe" && ext != ".msi" && ext != ".zip")
            {
                ValidationError = "Installer must be .exe, .msi, or .zip file.";
                return false;
            }
        }
        else if (IsScriptMode)
        {
            // For scripts, either inline content OR file path must be provided
            var hasContent = !string.IsNullOrWhiteSpace(ScriptContent);
            var hasFile = !string.IsNullOrWhiteSpace(FilePath);

            if (!hasContent && !hasFile)
            {
                ValidationError = "Either script content or file path is required.";
                return false;
            }

            if (hasFile && !File.Exists(FilePath))
            {
                ValidationError = "The specified script file does not exist.";
                return false;
            }
        }

        ValidationError = null;
        return true;
    }
}

public record IconOption(string Key, string DisplayName);
