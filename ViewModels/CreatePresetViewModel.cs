using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using Microsoft.Win32;

namespace KlevaDeploy.ViewModels;

/// <summary>
/// ViewModel for creating or editing a deployment preset with a sliding panel UI.
/// </summary>
public sealed partial class CreatePresetViewModel : ObservableObject
{
    private readonly IPresetIconService? _presetIconService;

    [ObservableProperty] private string _presetId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _icon = "📦";
    [ObservableProperty] private string? _customIconLightPath;
    [ObservableProperty] private string? _customIconDarkPath;
    [ObservableProperty] private bool _isIconPickerOpen;
    [ObservableProperty] private bool _useSeparateThemeIcons;
    [ObservableProperty] private bool _isIconPickerTargetDark;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _availableSearchText = string.Empty;
    [ObservableProperty] private string _selectedSearchText = string.Empty;
    [ObservableProperty] private string? _validationError;

    private DeploymentPreset? _editingPreset;
    public bool IsEditMode => _editingPreset != null;

    // Two columns logic
    public ObservableCollection<ProcessSelectionItem> AvailableProcesses { get; } = new();
    public ObservableCollection<ProcessSelectionItem> FilteredAvailableProcesses { get; } = new();
    
    public ObservableCollection<ProcessSelectionItem> SelectedProcesses { get; } = new();
    public ObservableCollection<ProcessSelectionItem> FilteredSelectedProcesses { get; } = new();

    // Available icons from Icons.xaml
    public ObservableCollection<string> AvailableIcons { get; } = new()
    {
        "📦", "🖥️", "📊", "💻", "🏢", "🖧", "⚙️", "🔧", 
        "📁", "📂", "🗂️", "💾", "🔒", "🔓", "✅", "⚠️"
    };

    public ObservableCollection<PresetIconLibraryItem> LibraryIcons { get; } = new();

    public event EventHandler? CloseRequested;

    public CreatePresetViewModel(IPresetIconService? presetIconService = null)
    {
        _presetIconService = presetIconService;
    }

    public DeploymentPreset? CreatedPreset { get; private set; }
    public bool HasCustomIcon => !string.IsNullOrWhiteSpace(CustomIconLightPath) || !string.IsNullOrWhiteSpace(CustomIconDarkPath);
    public bool IsSelectedReorderEnabled => string.IsNullOrWhiteSpace(SelectedSearchText);

    /// <summary>
    /// Initialize the ViewModel with available processes for creating a new preset.
    /// </summary>
    public void Initialize(IEnumerable<DeploymentProcess> availableProcesses)
    {
        try
        {
            _editingPreset = null;
            PresetId = Guid.NewGuid().ToString("N");
            Name = string.Empty;
            Icon = "📦";
            CustomIconLightPath = null;
            CustomIconDarkPath = null;
            IsIconPickerOpen = false;
            UseSeparateThemeIcons = false;
            IsIconPickerTargetDark = false;
            Description = string.Empty;
            Category = string.Empty;
            
            AvailableProcesses.Clear();
            SelectedProcesses.Clear();

            if (availableProcesses != null)
            {
                foreach (var process in availableProcesses)
                {
                    var item = new ProcessSelectionItem(process);
                    AvailableProcesses.Add(item);
                }
            }
            
            ApplyAvailableFilter();
            ApplySelectedFilter();
            OnPropertyChanged(nameof(IsEditMode));
            ValidationError = null;
        }
        catch (Exception)
        {
            ValidationError = "Errore durante l'inizializzazione del pannello.";
            // Nota: qui potremmo iniettare ILogService se volessimo loggare anche qui
        }
    }

    /// <summary>
    /// Initialize the ViewModel for editing an existing preset.
    /// </summary>
    public void InitializeForEdit(DeploymentPreset preset, IEnumerable<DeploymentProcess> availableProcesses)
    {
        try
        {
            if (preset == null) return;

            _editingPreset = preset;
            PresetId = preset.Id;
            Name = preset.Name;
            Icon = preset.Icon;
            CustomIconLightPath = preset.CustomIconLightPath;
            CustomIconDarkPath = preset.CustomIconDarkPath;
            IsIconPickerOpen = false;
            UseSeparateThemeIcons = false;
            IsIconPickerTargetDark = false;
            Description = preset.Description;
            Category = preset.Category;
            
            AvailableProcesses.Clear();
            SelectedProcesses.Clear();
            
            var stepDict = preset.Steps?.ToDictionary(s => s.ProcessId) ?? new();
            var orderedSteps = preset.Steps?.OrderBy(s => s.Order).ToList() ?? new();
            
            // Add to selected in order
            foreach (var step in orderedSteps)
            {
                var process = availableProcesses?.FirstOrDefault(p => p.Id == step.ProcessId);
                if (process != null)
                {
                    var item = new ProcessSelectionItem(process)
                    {
                        IsSelected = true,
                        Order = step.Order,
                        IsRequired = step.IsRequired
                    };
                    SelectedProcesses.Add(item);
                }
            }

            // Add remaining to available
            if (availableProcesses != null)
            {
                foreach (var process in availableProcesses)
                {
                    if (!stepDict.ContainsKey(process.Id))
                    {
                        var item = new ProcessSelectionItem(process);
                        AvailableProcesses.Add(item);
                    }
                }
            }
            
            ApplyAvailableFilter();
            ApplySelectedFilter();
            OnPropertyChanged(nameof(IsEditMode));
            ValidationError = null;
        }
        catch (Exception)
        {
            ValidationError = "Errore durante il caricamento del preset per la modifica.";
        }
    }

    partial void OnCustomIconLightPathChanged(string? value) => OnPropertyChanged(nameof(HasCustomIcon));
    partial void OnCustomIconDarkPathChanged(string? value) => OnPropertyChanged(nameof(HasCustomIcon));

    partial void OnUseSeparateThemeIconsChanged(bool value)
    {
        if (value) return;

        if (!string.IsNullOrWhiteSpace(CustomIconLightPath))
        {
            CustomIconDarkPath = CustomIconLightPath;
            return;
        }

        if (!string.IsNullOrWhiteSpace(CustomIconDarkPath))
        {
            CustomIconLightPath = CustomIconDarkPath;
        }
    }

    partial void OnAvailableSearchTextChanged(string value) => ApplyAvailableFilter();
    partial void OnSelectedSearchTextChanged(string value)
    {
        ApplySelectedFilter();
        OnPropertyChanged(nameof(IsSelectedReorderEnabled));
    }

    private void ApplyAvailableFilter()
    {
        FilteredAvailableProcesses.Clear();
        var searchLower = AvailableSearchText?.ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(searchLower)
            ? AvailableProcesses
            : AvailableProcesses.Where(p =>
                p.Name.ToLowerInvariant().Contains(searchLower) ||
                p.Description.ToLowerInvariant().Contains(searchLower));

        foreach (var process in filtered) FilteredAvailableProcesses.Add(process);
    }

    private void ApplySelectedFilter()
    {
        FilteredSelectedProcesses.Clear();
        var searchLower = SelectedSearchText?.ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(searchLower)
            ? SelectedProcesses.OrderBy(p => p.Order)
            : SelectedProcesses.Where(p =>
                p.Name.ToLowerInvariant().Contains(searchLower) ||
                p.Description.ToLowerInvariant().Contains(searchLower))
                .OrderBy(p => p.Order);

        foreach (var process in filtered) FilteredSelectedProcesses.Add(process);
    }

    [RelayCommand]
    private void ActivateProcess(ProcessSelectionItem item)
    {
        if (item.IsSelected || SelectedProcesses.Contains(item))
        {
            ValidationError = null;
            return;
        }

        AvailableProcesses.Remove(item);
        item.IsSelected = true;
        
        // Find max order and add 10
        int maxOrder = SelectedProcesses.Any() ? SelectedProcesses.Max(p => p.Order) : 0;
        item.Order = maxOrder + 10;
        
        SelectedProcesses.Add(item);
        
        ApplyAvailableFilter();
        ApplySelectedFilter();
        ValidationError = null;
    }

    [RelayCommand]
    private void DeactivateProcess(ProcessSelectionItem item)
    {
        if (!item.IsSelected || !SelectedProcesses.Contains(item))
        {
            ValidationError = null;
            return;
        }

        SelectedProcesses.Remove(item);
        item.IsSelected = false;
        item.Order = 0;
        item.IsRequired = false;
        
        AvailableProcesses.Add(item);
        
        // Re-order remaining selected
        UpdateProcessOrdering();
        
        ApplyAvailableFilter();
        ApplySelectedFilter();
    }

    private void UpdateProcessOrdering()
    {
        var ordered = SelectedProcesses.OrderBy(p => p.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = (i + 1) * 10;
        }
    }

    [RelayCommand]
    private void MoveProcessUp(ProcessSelectionItem item)
    {
        var ordered = SelectedProcesses.OrderBy(p => p.Order).ToList();
        var index = ordered.IndexOf(item);
        
        if (index > 0)
        {
            var temp = ordered[index].Order;
            ordered[index].Order = ordered[index - 1].Order;
            ordered[index - 1].Order = temp;
            ApplySelectedFilter();
        }
    }

    [RelayCommand]
    private void MoveProcessDown(ProcessSelectionItem item)
    {
        var ordered = SelectedProcesses.OrderBy(p => p.Order).ToList();
        var index = ordered.IndexOf(item);
        
        if (index < ordered.Count - 1)
        {
            var temp = ordered[index].Order;
            ordered[index].Order = ordered[index + 1].Order;
            ordered[index + 1].Order = temp;
            ApplySelectedFilter();
        }
    }

    [RelayCommand]
    private void ReorderSelectedProcess(ProcessReorderRequest? request)
    {
        if (request is null)
        {
            return;
        }

        var source = request.Source;
        var target = request.Target;

        if (target is not null && ReferenceEquals(source, target))
        {
            return;
        }

        var ordered = SelectedProcesses.OrderBy(p => p.Order).ToList();
        var sourceIndex = ordered.IndexOf(source);
        if (sourceIndex < 0)
        {
            return;
        }

        ordered.RemoveAt(sourceIndex);

        if (target is null)
        {
            ordered.Add(source);
        }
        else
        {
            var targetIndex = ordered.IndexOf(target);
            if (targetIndex < 0)
            {
                ordered.Add(source);
            }
            else
            {
                var insertIndex = request.InsertAfter ? targetIndex + 1 : targetIndex;
                insertIndex = Math.Clamp(insertIndex, 0, ordered.Count);
                ordered.Insert(insertIndex, source);
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = (i + 1) * 10;
            ordered[i].IsDragging = false;
        }

        ApplySelectedFilter();
        ValidationError = null;
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateInput()) return;

        CreatedPreset = CreatePreset();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        CreatedPreset = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenIconPicker()
    {
        LoadLibraryIcons();
        IsIconPickerOpen = true;
        ValidationError = null;
    }

    [RelayCommand]
    private void CloseIconPicker() => IsIconPickerOpen = false;

    [RelayCommand]
    private void SelectEmojiIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return;
        Icon = icon;
        CustomIconLightPath = null;
        CustomIconDarkPath = null;
        IsIconPickerOpen = false;
        ValidationError = null;
    }

    [RelayCommand]
    private void ImportLibraryIcon()
    {
        ValidationError = null;

        var path = PickIconFile("Seleziona file icona");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (_presetIconService == null)
            {
                ValidationError = "Servizio icone non disponibile.";
                return;
            }

            var item = _presetIconService.ImportLibraryIcon(path);
            LibraryIcons.Insert(0, item);
            SelectLibraryIcon(item);
        }
        catch (Exception ex)
        {
            ValidationError = $"Errore durante l'import icona: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SelectLibraryIcon(PresetIconLibraryItem? item)
    {
        if (item is null) return;
        if (string.IsNullOrWhiteSpace(item.LightPath) && string.IsNullOrWhiteSpace(item.DarkPath)) return;

        if (!UseSeparateThemeIcons)
        {
            var candidate = !string.IsNullOrWhiteSpace(item.LightPath) ? item.LightPath : item.DarkPath;
            CustomIconLightPath = candidate;
            CustomIconDarkPath = !string.IsNullOrWhiteSpace(item.DarkPath) ? item.DarkPath : candidate;
            IsIconPickerOpen = false;
            ValidationError = null;
            return;
        }

        if (IsIconPickerTargetDark)
        {
            CustomIconDarkPath = !string.IsNullOrWhiteSpace(item.DarkPath) ? item.DarkPath : item.LightPath;
        }
        else
        {
            CustomIconLightPath = !string.IsNullOrWhiteSpace(item.LightPath) ? item.LightPath : item.DarkPath;
        }

        ValidationError = null;
    }

    [RelayCommand]
    private void RemoveCustomIcon()
    {
        ValidationError = null;

        CustomIconLightPath = null;
        CustomIconDarkPath = null;
    }

    [RelayCommand]
    private void SetIconPickerTargetLight() => IsIconPickerTargetDark = false;

    [RelayCommand]
    private void SetIconPickerTargetDark() => IsIconPickerTargetDark = true;

    private static string? PickIconFile(string title)
    {
        var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.ico",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void LoadLibraryIcons()
    {
        LibraryIcons.Clear();
        if (_presetIconService == null) return;

        foreach (var item in _presetIconService.GetLibraryIcons())
        {
            LibraryIcons.Add(item);
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Il nome del preset è obbligatorio.";
            return false;
        }

        if (SelectedProcesses.Count == 0)
        {
            ValidationError = "Seleziona almeno un processo per il preset.";
            return false;
        }

        ValidationError = null;
        return true;
    }

    private DeploymentPreset CreatePreset()
    {
        var steps = SelectedProcesses
            .OrderBy(p => p.Order)
            .Select(p => new PresetProcessStep
            {
                ProcessId = p.Process.Id,
                Order = p.Order,
                EnabledOverride = null,
                IsRequired = p.IsRequired
            }).ToList();

        if (IsEditMode && _editingPreset != null)
        {
            _editingPreset.Name = Name.Trim();
            _editingPreset.Icon = Icon;
            _editingPreset.CustomIconLightPath = CustomIconLightPath;
            _editingPreset.CustomIconDarkPath = CustomIconDarkPath;
            _editingPreset.Description = Description.Trim();
            _editingPreset.Category = string.IsNullOrWhiteSpace(Category) ? "Personalizzato" : Category.Trim();
            _editingPreset.Steps = steps;
            return _editingPreset;
        }
        
        return new DeploymentPreset
        {
            Id = PresetId,
            Name = Name.Trim(),
            Icon = Icon,
            CustomIconLightPath = CustomIconLightPath,
            CustomIconDarkPath = CustomIconDarkPath,
            Description = Description.Trim(),
            Category = string.IsNullOrWhiteSpace(Category) ? "Personalizzato" : Category.Trim(),
            Steps = steps
        };
    }
}
