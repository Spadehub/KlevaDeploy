using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.ViewModels;

public sealed class ProcessStepViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;

    public DeploymentProcess Process { get; }
    private int _order;
    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }
    public bool IsRequired { get; }

    private bool _isInSelectedPreset;
    public bool IsInSelectedPreset
    {
        get => _isInSelectedPreset;
        set => SetProperty(ref _isInSelectedPreset, value);
    }

    // Manual property — intercepts attempts to disable a required step.
    private bool _isEnabled;
    private bool _suppressWarning = false;
    
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            // Only show warning if:
            // 1. User is trying to turn OFF a required step
            // 2. The step IS currently in a selected preset
            // 3. The change is from user interaction (not programmatic)
            // 4. Warning is not suppressed
            if (!value && IsRequired && IsInSelectedPreset && !_suppressWarning && _isEnabled != value)
            {
                var confirmed = _dialogService.ShowDisableRequiredWarning(Process.Name);
                if (!confirmed) return;   // User cancelled — keep the step enabled.
            }
            SetProperty(ref _isEnabled, value);
        }
    }
    
    /// <summary>
    /// Sets IsEnabled without triggering the warning dialog (for programmatic changes).
    /// </summary>
    public void SetIsEnabledSilently(bool value)
    {
        _suppressWarning = true;
        IsEnabled = value;
        _suppressWarning = false;
    }

    private string _statusIcon = "⏳";
    public string StatusIcon
    {
        get => _statusIcon;
        set => SetProperty(ref _statusIcon, value);
    }

    private string _statusText = "In attesa";
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private bool _isRunningStep;
    public bool IsRunningStep
    {
        get => _isRunningStep;
        set => SetProperty(ref _isRunningStep, value);
    }

    private bool _wasSkippedThisRun;
    public bool WasSkippedThisRun
    {
        get => _wasSkippedThisRun;
        set => SetProperty(ref _wasSkippedThisRun, value);
    }

    private double _progressValue;
    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    private bool _isProgressIndeterminate;
    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    private string _currentSubProcessName = string.Empty;
    public string CurrentSubProcessName
    {
        get => _currentSubProcessName;
        set => SetProperty(ref _currentSubProcessName, value);
    }

    private bool _isSubProcessRunning;
    public bool IsSubProcessRunning
    {
        get => _isSubProcessRunning;
        set => SetProperty(ref _isSubProcessRunning, value);
    }

    private double _subProgressValue;
    public double SubProgressValue
    {
        get => _subProgressValue;
        set => SetProperty(ref _subProgressValue, value);
    }

    private bool _isSubProgressIndeterminate;
    public bool IsSubProgressIndeterminate
    {
        get => _isSubProgressIndeterminate;
        set => SetProperty(ref _isSubProgressIndeterminate, value);
    }

    private int _executionIndex;
    public int ExecutionIndex
    {
        get => _executionIndex;
        set => SetProperty(ref _executionIndex, value);
    }

    public string Name => Process.Name;
    public string Description => Process.Description;
    public string KindLabel => Process.Kind switch
    {
        ProcessKind.Installer      => "Installer",
        ProcessKind.PowerShellScript => "PowerShell",
        ProcessKind.BatchScript    => "Batch",
        ProcessKind.BashScript     => "Bash",
        ProcessKind.RegistryFile   => "Registry",
        ProcessKind.ConfigAction   => "Config",
        _                          => "Unknown"
    };

    public bool HasCustomIcon =>
        !string.IsNullOrWhiteSpace(Process.CustomIconLightPath) ||
        !string.IsNullOrWhiteSpace(Process.CustomIconDarkPath);

    public bool HasEmojiIcon => !HasCustomIcon && !string.IsNullOrWhiteSpace(Process.Icon);
    public bool ShowDefaultIcon => !HasCustomIcon && !HasEmojiIcon;
    public string Icon => Process.Icon;
    public string? CustomIconLightPath => Process.CustomIconLightPath;
    public string? CustomIconDarkPath => Process.CustomIconDarkPath;

    private bool _showLoginBadge;
    public bool ShowLoginBadge
    {
        get => _showLoginBadge;
        set => SetProperty(ref _showLoginBadge, value);
    }

    public ProcessStepViewModel(DeploymentProcess process, int order, IDialogService dialogService, bool isInSelectedPreset = true, bool isRequired = false)
    {
        Process = process;
        _order = order;
        IsRequired = isRequired;
        _dialogService = dialogService;
        _isEnabled = process.EnabledByDefault;
        _isInSelectedPreset = isInSelectedPreset;
    }

    public void SetStatus(string icon, string text)
    {
        StatusIcon = icon;
        StatusText = text;
    }

    public void ResetProgress()
    {
        IsRunningStep = false;
        WasSkippedThisRun = false;
        ProgressValue = 0;
        IsProgressIndeterminate = false;
        CurrentSubProcessName = string.Empty;
        IsSubProcessRunning = false;
        SubProgressValue = 0;
        IsSubProgressIndeterminate = false;
    }
}
