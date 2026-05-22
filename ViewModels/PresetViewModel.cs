using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Models;

namespace KlevaDeploy.ViewModels;

public sealed partial class PresetViewModel : ObservableObject
{
    public DeploymentPreset Preset { get; }

    [ObservableProperty] private bool _isSelected;

    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string Category => Preset.Category;
    public string Icon => Preset.Icon;
    public bool HasEmojiIcon => !string.IsNullOrWhiteSpace(Icon);
    public int StepCount => Preset.Steps.Count;

    public PresetViewModel(DeploymentPreset preset) => Preset = preset;
}
