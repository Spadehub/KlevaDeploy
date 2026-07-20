using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Models;

namespace KlevaDeploy.ViewModels;

public enum ArgumentPromptMode
{
    None,
    RunOnce,
    RunAlways
}

public sealed partial class ArgumentPromptItemViewModel : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string Description { get; }
    public bool IsSecret { get; }
    public bool IsRequired { get; }

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    private string replacementHint = string.Empty;

    [ObservableProperty]
    private bool isPasswordVisible;

    public ArgumentPromptItemViewModel(ArgumentInputDefinition def, string initialValue)
    {
        Key = (def.Key ?? string.Empty).Trim();
        Label = string.IsNullOrWhiteSpace(def.Label) ? Key : def.Label.Trim();
        Description = (def.Description ?? string.Empty).Trim();
        IsSecret = def.IsSecret;
        IsRequired = def.IsRequired;
        Value = initialValue ?? string.Empty;
        UpdateReplacementHint();
    }

    partial void OnValueChanged(string value)
    {
        UpdateReplacementHint();
    }

    private void UpdateReplacementHint()
    {
        if (IsSecret)
        {
            ReplacementHint = string.Empty;
            return;
        }

        var raw = Value ?? string.Empty;
        if (!raw.Contains('%', StringComparison.Ordinal))
        {
            ReplacementHint = string.Empty;
            return;
        }

        string expanded;
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(raw);
        }
        catch
        {
            expanded = raw;
        }

        if (string.Equals(expanded, raw, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(expanded))
        {
            ReplacementHint = string.Empty;
            return;
        }

        ReplacementHint = $"Verrà sostituito con: [{expanded}]";
    }
}

public sealed partial class ArgumentPromptViewModel : ObservableObject
{
    public string ProcessName { get; }
    public string Subtitle { get; }
    public ObservableCollection<ArgumentPromptItemViewModel> Items { get; } = new();

    [ObservableProperty]
    private bool isValid;

    public ArgumentPromptMode Mode { get; private set; } = ArgumentPromptMode.None;

    public ArgumentPromptViewModel(string processName, string subtitle, IEnumerable<ArgumentInputDefinition> inputs, IReadOnlyDictionary<string, string> prefill)
    {
        ProcessName = string.IsNullOrWhiteSpace(processName) ? "Processo" : processName.Trim();
        Subtitle = subtitle ?? string.Empty;

        foreach (var def in inputs)
        {
            if (def is null) continue;
            var key = (def.Key ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            var candidate =
                (prefill.TryGetValue(key, out var v) ? v : null) ??
                (def.DefaultValue ?? string.Empty);

            var item = new ArgumentPromptItemViewModel(def, candidate);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }

        RecomputeIsValid();
    }

    public void Choose(ArgumentPromptMode mode)
    {
        Mode = mode;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArgumentPromptItemViewModel.Value))
            RecomputeIsValid();
    }

    private void RecomputeIsValid()
    {
        foreach (var i in Items)
        {
            if (!i.IsRequired) continue;
            if (string.IsNullOrWhiteSpace(i.Value))
            {
                IsValid = false;
                return;
            }
        }

        IsValid = true;
    }
}
