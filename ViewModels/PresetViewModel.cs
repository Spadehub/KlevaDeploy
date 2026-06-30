using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Models;

namespace KlevaDeploy.ViewModels;

public sealed class PresetViewModel : ObservableObject
{
    private int _missingStepCount;
    private string _missingProcessSummary = string.Empty;

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
    public int MissingStepCount => _missingStepCount;
    public bool HasMissingProcesses => _missingStepCount > 0;
    public int ResolvedStepCount => Math.Max(0, StepCount - _missingStepCount);
    public string MissingProcessSummary => _missingProcessSummary;

    public PresetViewModel(DeploymentPreset preset) => Preset = preset;

    public void UpdateProcessAvailability(IReadOnlySet<string> availableProcessIds)
    {
        var missingSteps = Preset.Steps
            .Where(step => !string.IsNullOrWhiteSpace(step.ProcessId) && !availableProcessIds.Contains(step.ProcessId))
            .ToList();

        _missingStepCount = missingSteps.Count;
        _missingProcessSummary = missingSteps.Count == 0
            ? string.Empty
            : string.Join(", ", missingSteps
                .Select(step => step.ProcessId)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(MissingStepCount));
        OnPropertyChanged(nameof(HasMissingProcesses));
        OnPropertyChanged(nameof(ResolvedStepCount));
        OnPropertyChanged(nameof(MissingProcessSummary));
    }

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
        OnPropertyChanged(nameof(MissingStepCount));
        OnPropertyChanged(nameof(HasMissingProcesses));
        OnPropertyChanged(nameof(ResolvedStepCount));
        OnPropertyChanged(nameof(MissingProcessSummary));
    }
}
