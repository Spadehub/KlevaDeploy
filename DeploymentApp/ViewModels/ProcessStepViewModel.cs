using CommunityToolkit.Mvvm.ComponentModel;
using DeploymentApp.Models;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.ViewModels;

public sealed partial class ProcessStepViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;

    public DeploymentProcess Process { get; }
    public int Order { get; }
    public bool IsRequired { get; }

    [ObservableProperty] private bool _isInSelectedPreset;

    // Manual property — intercepts attempts to disable a required step.
    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            // Only show warning if user is trying to turn OFF a required step
            // that IS currently in a selected preset (not when presets are deselected)
            if (!value && IsRequired && IsInSelectedPreset)
            {
                var confirmed = _dialogService.ShowDisableRequiredWarning(Process.Name);
                if (!confirmed) return;   // User cancelled — keep the step enabled.
            }
            SetProperty(ref _isEnabled, value);
        }
    }

    [ObservableProperty] private string _statusIcon = "⏳";
    [ObservableProperty] private string _statusText = "In attesa";

    public string Name => Process.Name;
    public string Description => Process.Description;
    public string KindLabel => Process.Kind switch
    {
        ProcessKind.Installer      => "Installer",
        ProcessKind.PowerShellScript => "PowerShell",
        ProcessKind.BatchScript    => "Batch",
        ProcessKind.RegistryFile   => "Registry",
        ProcessKind.ConfigAction   => "Config",
        _                          => "Unknown"
    };

    public ProcessStepViewModel(DeploymentProcess process, int order, IDialogService dialogService, bool isInSelectedPreset = true, bool isRequired = false)
    {
        Process = process;
        Order = order;
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
}
