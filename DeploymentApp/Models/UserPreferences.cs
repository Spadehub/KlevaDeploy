namespace DeploymentApp.Models;

/// <summary>
/// Static class to store user preferences in-memory.
/// Can be extended later to persist to file/registry.
/// </summary>
public static class UserPreferences
{
    /// <summary>
    /// Gets or sets whether the user has chosen to suppress the "disable required process" warning dialog.
    /// </summary>
    public static bool SuppressRequiredProcessWarning { get; set; } = false;
}
