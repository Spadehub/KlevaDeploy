using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Models;

namespace DeploymentApp.ViewModels;

/// <summary>
/// ViewModel for creating or editing a deployment preset with a sliding panel UI.
/// </summary>
public sealed partial class CreatePresetViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _icon = "📦";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _processSearchText = string.Empty;
    [ObservableProperty] private string? _validationError;

    private DeploymentPreset? _editingPreset;
    public bool IsEditMode => _editingPreset != null;

    public ObservableCollection<ProcessSelectionItem> AllProcesses { get; } = new();
    public ObservableCollection<ProcessSelectionItem> FilteredProcesses { get; } = new();
    public ObservableCollection<string> CategorySuggestions { get; } = new()
    {
        "Base",
        "Generale",
        "Ufficio",
        "Sviluppo",
        "Server",
        "Tecnico"
    };

    // Available icons from Icons.xaml
    public ObservableCollection<string> AvailableIcons { get; } = new()
    {
        "📦", "🖥️", "📊", "💻", "🏢", "🖧", "⚙️", "🔧", 
        "📁", "📂", "🗂️", "💾", "🔒", "🔓", "✅", "⚠️"
    };

    public event EventHandler? CloseRequested;

    public CreatePresetViewModel()
    {
    }

    /// <summary>
    /// Initialize the ViewModel with available processes for creating a new preset.
    /// </summary>
    public void Initialize(IEnumerable<DeploymentProcess> availableProcesses)
    {
        _editingPreset = null;
        Name = string.Empty;
        Icon = "📦";
        Description = string.Empty;
        Category = string.Empty;
        
        AllProcesses.Clear();
        foreach (var process in availableProcesses)
        {
            var item = new ProcessSelectionItem(process);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessSelectionItem.IsSelected))
                {
                    UpdateProcessOrdering();
                    ValidationError = null;
                }
            };
            AllProcesses.Add(item);
        }
        ApplyProcessFilter();
        OnPropertyChanged(nameof(IsEditMode));
    }

    /// <summary>
    /// Initialize the ViewModel for editing an existing preset.
    /// </summary>
    public void InitializeForEdit(DeploymentPreset preset, IEnumerable<DeploymentProcess> availableProcesses)
    {
        _editingPreset = preset;
        Name = preset.Name;
        Icon = preset.Icon;
        Description = preset.Description;
        Category = preset.Category;
        
        AllProcesses.Clear();
        
        // Create a dictionary of process steps for quick lookup
        var stepDict = preset.Steps.ToDictionary(s => s.ProcessId);
        
        foreach (var process in availableProcesses)
        {
            var item = new ProcessSelectionItem(process);
            
            // If this process is in the preset, mark it as selected and set its properties
            if (stepDict.TryGetValue(process.Id, out var step))
            {
                item.IsSelected = true;
                item.Order = step.Order;
                item.IsRequired = step.IsRequired;
            }
            
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessSelectionItem.IsSelected))
                {
                    UpdateProcessOrdering();
                    ValidationError = null;
                }
            };
            AllProcesses.Add(item);
        }
        
        ApplyProcessFilter();
        OnPropertyChanged(nameof(IsEditMode));
    }

    partial void OnProcessSearchTextChanged(string value)
    {
        ApplyProcessFilter();
    }

    private void ApplyProcessFilter()
    {
        FilteredProcesses.Clear();
        var searchLower = ProcessSearchText?.ToLowerInvariant() ?? string.Empty;

        var filtered = string.IsNullOrWhiteSpace(searchLower)
            ? AllProcesses
            : AllProcesses.Where(p =>
                p.Name.ToLowerInvariant().Contains(searchLower) ||
                p.Description.ToLowerInvariant().Contains(searchLower) ||
                p.KindLabel.ToLowerInvariant().Contains(searchLower));

        foreach (var process in filtered)
        {
            FilteredProcesses.Add(process);
        }
    }

    private void UpdateProcessOrdering()
    {
        var selected = AllProcesses.Where(p => p.IsSelected).ToList();
        for (int i = 0; i < selected.Count; i++)
        {
            selected[i].Order = (i + 1) * 10;
        }
    }

    [RelayCommand]
    private void MoveProcessUp(ProcessSelectionItem item)
    {
        var selected = AllProcesses.Where(p => p.IsSelected).OrderBy(p => p.Order).ToList();
        var index = selected.IndexOf(item);
        
        if (index > 0)
        {
            // Swap orders
            var temp = selected[index].Order;
            selected[index].Order = selected[index - 1].Order;
            selected[index - 1].Order = temp;
        }
    }

    [RelayCommand]
    private void MoveProcessDown(ProcessSelectionItem item)
    {
        var selected = AllProcesses.Where(p => p.IsSelected).OrderBy(p => p.Order).ToList();
        var index = selected.IndexOf(item);
        
        if (index < selected.Count - 1)
        {
            // Swap orders
            var temp = selected[index].Order;
            selected[index].Order = selected[index + 1].Order;
            selected[index + 1].Order = temp;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!ValidateInput())
        {
            return;
        }

        var preset = CreatePreset();
        CreatedPreset = preset;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        CreatedPreset = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Il nome del preset è obbligatorio.";
            return false;
        }

        var selectedProcesses = AllProcesses.Where(p => p.IsSelected).ToList();
        if (selectedProcesses.Count == 0)
        {
            ValidationError = "Seleziona almeno un processo per il preset.";
            return false;
        }

        ValidationError = null;
        return true;
    }

    private DeploymentPreset CreatePreset()
    {
        var selectedProcesses = AllProcesses
            .Where(p => p.IsSelected)
            .OrderBy(p => p.Order)
            .ToList();

        var steps = selectedProcesses.Select(p => new PresetProcessStep
        {
            ProcessId = p.Process.Id,
            Order = p.Order,
            EnabledOverride = null,
            IsRequired = p.IsRequired
        }).ToList();

        if (IsEditMode && _editingPreset != null)
        {
            // Update existing preset
            _editingPreset.Name = Name.Trim();
            _editingPreset.Icon = Icon;
            _editingPreset.Description = Description.Trim();
            _editingPreset.Category = string.IsNullOrWhiteSpace(Category) ? "Personalizzato" : Category.Trim();
            _editingPreset.Steps = steps;
            return _editingPreset;
        }
        else
        {
            // Create new preset
            return new DeploymentPreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = Name.Trim(),
                Icon = Icon,
                Description = Description.Trim(),
                Category = string.IsNullOrWhiteSpace(Category) ? "Personalizzato" : Category.Trim(),
                Steps = steps
            };
        }
    }

    public DeploymentPreset? CreatedPreset { get; private set; }
}
