using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Models;

namespace KlevaDeploy.ViewModels;

/// <summary>
/// Wraps a <see cref="PackageFeature"/> for display in the right-hand features panel.
/// Handles the "disable required feature" warning with a persistent "don't show again" flag.
/// </summary>
public sealed class FeatureItemViewModel : ObservableObject
{
    // Shared across all instances — persists for the lifetime of the app session
    private static bool _suppressRequiredWarning;

    public PackageFeature Feature { get; }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>
    /// Raised when the user attempts to disable a required feature and needs a confirmation dialog.
    /// The handler must set <see cref="RequiredFeatureWarningEventArgs.Confirmed"/> before returning.
    /// </summary>
    public event EventHandler<RequiredFeatureWarningEventArgs>? RequiredFeatureWarningRequested;

    public FeatureItemViewModel(PackageFeature feature)
    {
        Feature = feature;
        _isEnabled = true; // enabled by default when preset is selected
    }

    /// <summary>
    /// Called by the View's CheckBox Unchecked event (via code-behind) to intercept
    /// the "uncheck required" scenario before the property is committed.
    /// Returns false if the change should be rejected (user cancelled).
    /// </summary>
    public bool RequestSetEnabled(bool newValue)
    {
        if (!newValue && Feature.IsNeeded && !_suppressRequiredWarning)
        {
            var args = new RequiredFeatureWarningEventArgs(Feature.Name);
            RequiredFeatureWarningRequested?.Invoke(this, args);

            if (!args.Confirmed) return false; // reject — keep enabled
            if (args.DontShowAgain) _suppressRequiredWarning = true;
        }
        IsEnabled = newValue;
        return true;
    }

    public string KindIcon => Feature.Kind switch
    {
        FeatureKind.MainInstall => "📦",
        FeatureKind.Script => Feature.ScriptType switch
        {
            ScriptType.PowerShell => "⚡",
            ScriptType.Batch => "⚙",
            ScriptType.Registry => "🔧",
            _ => "📄"
        },
        _ => "📄"
    };

    public bool IsRequired => Feature.IsNeeded;
}

public sealed class RequiredFeatureWarningEventArgs : EventArgs
{
    public string FeatureName { get; }
    /// <summary>Set to true by the dialog handler if the user confirmed disabling.</summary>
    public bool Confirmed { get; set; }
    /// <summary>Set to true by the dialog handler if the user checked "don't show again".</summary>
    public bool DontShowAgain { get; set; }

    public RequiredFeatureWarningEventArgs(string featureName) => FeatureName = featureName;
}
