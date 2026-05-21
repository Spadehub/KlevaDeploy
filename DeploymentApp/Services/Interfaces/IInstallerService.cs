using DeploymentApp.Models;

namespace DeploymentApp.Services.Interfaces;

public interface IInstallerService
{
    Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync(bool isDemoMode);
    Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync(bool isDemoMode);
    /// <summary>
    /// Given selected presets, returns the merged, deduplicated, ordered list of process steps.
    /// If a process appears in multiple presets, use the lowest Order value.
    /// </summary>
    IReadOnlyList<(DeploymentProcess Process, int Order)> BuildExecutionQueue(
        IEnumerable<DeploymentPreset> selectedPresets,
        IReadOnlyList<DeploymentProcess> allProcesses);
    string ResolveProcessPath(DeploymentProcess process);
    
    /// <summary>
    /// Adds a user-created preset to the in-memory collection.
    /// </summary>
    void AddUserPreset(DeploymentPreset preset);
    
    /// <summary>
    /// Gets all available processes (both demo/production and user-created).
    /// </summary>
    IReadOnlyList<DeploymentProcess> GetAllAvailableProcesses();
    
    /// <summary>
    /// Gets all presets (both demo/production and user-created).
    /// </summary>
    IReadOnlyList<DeploymentPreset> GetAllPresets();
}
