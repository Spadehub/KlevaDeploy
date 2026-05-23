using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeploymentApp.Models;

namespace DeploymentApp.ViewModels;

/// <summary>
/// ViewModel for creating or editing a deployment preset with a sliding panel UI.
/// </summary>
public sealed class CreatePresetViewModel : ObservableObject
{
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private string _icon = "📦";
    public string Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    private string _category = string.Empty;
    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    private string _availableSearchText = string.Empty;
    public string AvailableSearchText
    {
        get => _availableSearchText;
        set
        {
            if (!SetProperty(ref _availableSearchText, value)) return;
            ApplyAvailableFilter();
        }
    }

    private string _selectedSearchText = string.Empty;
    public string SelectedSearchText
    {
        get => _selectedSearchText;
        set
        {
            if (!SetProperty(ref _selectedSearchText, value)) return;
            SelectedProcessesView.Refresh();
            OnPropertyChanged(nameof(IsSelectedReorderEnabled));
        }
    }

    private string? _validationError;
    public string? ValidationError
    {
        get => _validationError;
        set => SetProperty(ref _validationError, value);
    }

    private DeploymentPreset? _editingPreset;
    public bool IsEditMode => _editingPreset != null;

    // Two columns logic
    public ObservableCollection<ProcessSelectionItem> AvailableProcesses { get; } = new();
    public ObservableCollection<ProcessSelectionItem> FilteredAvailableProcesses { get; } = new();
    
    public ObservableCollection<ProcessSelectionItem> SelectedProcesses { get; } = new();
    public ICollectionView SelectedProcessesView { get; }

    // Available icons from Icons.xaml
    public ObservableCollection<string> AvailableIcons { get; } = new()
    {
        "📦", "🖥️", "📊", "💻", "🏢", "🖧", "⚙️", "🔧", 
        "📁", "📂", "🗂️", "💾", "🔒", "🔓", "✅", "⚠️"
    };

    public event EventHandler? CloseRequested;

    public CreatePresetViewModel()
    {
        SelectedProcessesView = CollectionViewSource.GetDefaultView(SelectedProcesses);
        SelectedProcessesView.Filter = FilterSelected;

        ActivateProcessCommand = new RelayCommand<ProcessSelectionItem?>(ActivateProcess);
        DeactivateProcessCommand = new RelayCommand<ProcessSelectionItem?>(DeactivateProcess);
        MoveProcessUpCommand = new RelayCommand<ProcessSelectionItem?>(MoveProcessUp);
        MoveProcessDownCommand = new RelayCommand<ProcessSelectionItem?>(MoveProcessDown);
        ReorderSelectedProcessCommand = new RelayCommand<ProcessReorderRequest?>(ReorderSelectedProcess);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    public DeploymentPreset? CreatedPreset { get; private set; }

    public IRelayCommand<ProcessSelectionItem?> ActivateProcessCommand { get; }
    public IRelayCommand<ProcessSelectionItem?> DeactivateProcessCommand { get; }
    public IRelayCommand<ProcessSelectionItem?> MoveProcessUpCommand { get; }
    public IRelayCommand<ProcessSelectionItem?> MoveProcessDownCommand { get; }
    public IRelayCommand<ProcessReorderRequest?> ReorderSelectedProcessCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Initialize the ViewModel with available processes for creating a new preset.
    /// </summary>
    public void Initialize(IEnumerable<DeploymentProcess> availableProcesses)
    {
        try
        {
            _editingPreset = null;
            Name = string.Empty;
            Icon = "📦";
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
            UpdateProcessOrdering();
            SelectedProcessesView.Refresh();
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
            Name = preset.Name;
            Icon = preset.Icon;
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
            UpdateProcessOrdering();
            SelectedProcessesView.Refresh();
            OnPropertyChanged(nameof(IsEditMode));
            ValidationError = null;
        }
        catch (Exception)
        {
            ValidationError = "Errore durante il caricamento del preset per la modifica.";
        }
    }

    public bool IsSelectedReorderEnabled => string.IsNullOrWhiteSpace(SelectedSearchText);

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

    private bool FilterSelected(object obj)
    {
        if (obj is not ProcessSelectionItem item)
        {
            return false;
        }

        var searchLower = SelectedSearchText?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(searchLower))
        {
            return true;
        }

        return item.Name.ToLowerInvariant().Contains(searchLower) ||
               item.Description.ToLowerInvariant().Contains(searchLower);
    }

    private void ActivateProcess(ProcessSelectionItem? item)
    {
        if (item is null) return;
        AvailableProcesses.Remove(item);
        item.IsSelected = true;
        SelectedProcesses.Add(item);
        UpdateProcessOrdering();
        
        ApplyAvailableFilter();
        SelectedProcessesView.Refresh();
        ValidationError = null;
    }

    private void DeactivateProcess(ProcessSelectionItem? item)
    {
        if (item is null) return;
        SelectedProcesses.Remove(item);
        item.IsSelected = false;
        item.Order = 0;
        item.IsRequired = false;
        
        AvailableProcesses.Add(item);
        
        // Re-order remaining selected
        UpdateProcessOrdering();
        
        ApplyAvailableFilter();
        SelectedProcessesView.Refresh();
    }

    private void UpdateProcessOrdering()
    {
        for (int i = 0; i < SelectedProcesses.Count; i++)
        {
            SelectedProcesses[i].Order = (i + 1) * 10;
        }
    }

    private void ReorderSelectedProcess(ProcessReorderRequest? request)
    {
        if (request is null) return;
        if (ReferenceEquals(request.Source, request.Target)) return;
        if (!IsSelectedReorderEnabled) return;

        var oldIndex = SelectedProcesses.IndexOf(request.Source);
        if (oldIndex < 0) return;

        var targetIndex = request.Target is null ? SelectedProcesses.Count - 1 : SelectedProcesses.IndexOf(request.Target);
        if (targetIndex < 0) targetIndex = SelectedProcesses.Count - 1;

        var insertIndex = targetIndex + (request.InsertAfter ? 1 : 0);
        if (oldIndex < insertIndex) insertIndex--;
        if (insertIndex < 0) insertIndex = 0;
        if (insertIndex > SelectedProcesses.Count - 1) insertIndex = SelectedProcesses.Count - 1;

        if (oldIndex == insertIndex) return;

        SelectedProcesses.Move(oldIndex, insertIndex);
        UpdateProcessOrdering();
        SelectedProcessesView.Refresh();
    }

    private void MoveProcessUp(ProcessSelectionItem? item)
    {
        if (item is null) return;
        var index = SelectedProcesses.IndexOf(item);
        if (index <= 0) return;

        SelectedProcesses.Move(index, index - 1);
        UpdateProcessOrdering();
        SelectedProcessesView.Refresh();
    }

    private void MoveProcessDown(ProcessSelectionItem? item)
    {
        if (item is null) return;
        var index = SelectedProcesses.IndexOf(item);
        if (index < 0 || index >= SelectedProcesses.Count - 1) return;

        SelectedProcesses.Move(index, index + 1);
        UpdateProcessOrdering();
        SelectedProcessesView.Refresh();
    }

    private void Save()
    {
        if (!ValidateInput()) return;

        CreatedPreset = CreatePreset();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

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
            _editingPreset.Description = Description.Trim();
            _editingPreset.Category = string.IsNullOrWhiteSpace(Category) ? "Personalizzato" : Category.Trim();
            _editingPreset.Steps = steps;
            return _editingPreset;
        }
        
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
