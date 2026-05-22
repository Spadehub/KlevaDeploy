namespace KlevaDeploy.Models;

/// <summary>
/// Represents a single process/feature that belongs to a <see cref="SoftwarePackage"/> preset.
/// Features can be individual scripts, registry tweaks, or the main software installer itself.
/// </summary>
public class PackageFeature
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>The kind of action this feature performs.</summary>
    public FeatureKind Kind { get; set; } = FeatureKind.Script;

    /// <summary>
    /// When true this feature is required by the preset.
    /// Disabling it will trigger a confirmation warning.
    /// </summary>
    public bool IsNeeded { get; set; }

    /// <summary>
    /// The script or installer associated with this feature (optional).
    /// For Kind == MainInstall this maps to the parent SoftwarePackage installer.
    /// </summary>
    public string? ScriptRelativePath { get; set; }
    public ScriptType ScriptType { get; set; } = ScriptType.PowerShell;
}

public enum FeatureKind
{
    /// <summary>The primary software installer for the package.</summary>
    MainInstall,
    /// <summary>A configuration script (PowerShell / Batch / Registry).</summary>
    Script,
}
