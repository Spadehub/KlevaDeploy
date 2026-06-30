using CommunityToolkit.Mvvm.ComponentModel;

namespace KlevaDeploy.Models;

public sealed partial class MissingPresetProcessReference : ObservableObject
{
    public MissingPresetProcessReference(string processId, int order, bool isRequired)
    {
        ProcessId = processId;
        Order = order;
        IsRequired = isRequired;
    }

    public string ProcessId { get; }

    public int Order { get; }

    public bool IsRequired { get; }

    [ObservableProperty]
    private string? _selectedReplacementProcessId;
}
