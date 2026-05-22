using CommunityToolkit.Mvvm.ComponentModel;

namespace KlevaDeploy.Models;

/// <summary>
/// Helper class for process selection in preset creation.
/// Wraps a DeploymentProcess with selection state, ordering, and required flag.
/// </summary>
public sealed partial class ProcessSelectionItem : ObservableObject
{
    public DeploymentProcess Process { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isRequired;
    [ObservableProperty] private int _order;

    public ProcessSelectionItem(DeploymentProcess process)
    {
        Process = process;
    }

    public string Name => Process.Name;
    public string Description => Process.Description;
    public string IconKey => Process.IconKey;
    public string KindLabel => Process.Kind.ToString();
    public bool IsUserCreated => Process.IsUserCreated;
}
