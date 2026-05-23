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
    public string? CustomIconLightPath => Preset.CustomIconLightPath;
    public string? CustomIconDarkPath => Preset.CustomIconDarkPath;
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(CustomIconLightPath) || !string.IsNullOrWhiteSpace(CustomIconDarkPath);
    public bool HasEmojiIcon => !HasCustomIcon && !string.IsNullOrWhiteSpace(Icon);
    public bool ShowDefaultIcon => !HasCustomIcon && string.IsNullOrWhiteSpace(Icon);
    public int StepCount => Preset.Steps.Count;

    public PresetViewModel(DeploymentPreset preset) => Preset = preset;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(CustomIconLightPath));
        OnPropertyChanged(nameof(CustomIconDarkPath));
        OnPropertyChanged(nameof(HasCustomIcon));
        OnPropertyChanged(nameof(HasEmojiIcon));
        OnPropertyChanged(nameof(ShowDefaultIcon));
        OnPropertyChanged(nameof(StepCount));
    }
}
