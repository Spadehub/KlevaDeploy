using CommunityToolkit.Mvvm.ComponentModel;

namespace KlevaDeploy.Models;

/// <summary>
/// Helper class for process selection in preset creation.
/// Wraps a DeploymentProcess with selection state, ordering, and required flag.
/// </summary>
public sealed class ProcessSelectionItem : ObservableObject
{
    public DeploymentProcess Process { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isRequired;
    public bool IsRequired
    {
        get => _isRequired;
        set => SetProperty(ref _isRequired, value);
    }

    private int _order;
    public int Order
    {
        get => _order;
        set => SetProperty(ref _order, value);
    }

    private bool _isDragging;
    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }

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
