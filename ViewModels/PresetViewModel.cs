using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Models;

namespace KlevaDeploy.ViewModels;

public sealed class PresetViewModel : ObservableObject
{
    public DeploymentPreset Preset { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string Category => Preset.Category;
    public string Icon => Preset.Icon;
    public bool HasEmojiIcon => !string.IsNullOrWhiteSpace(Icon);
    public int StepCount => Preset.Steps.Count;

    public PresetViewModel(DeploymentPreset preset) => Preset = preset;
}
