using KlevaDeploy.Models;

namespace KlevaDeploy.Services.Interfaces;

public interface IInstallerService
{
    Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync(bool isDemoMode);
    Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync(bool isDemoMode);
    /// <summary>
    /// Given selected presets, returns the merged, deduplicated, ordered list of process steps.
    /// If a process appears in multiple presets, use the lowest Order value.
    /// IsRequired is true if ANY preset marks the process as required.
    /// </summary>
    IReadOnlyList<(DeploymentProcess Process, int Order, bool IsRequired)> BuildExecutionQueue(
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

    /// <summary>
    /// Adds a user-created process.
    /// </summary>
    void AddUserProcess(DeploymentProcess process);

    /// <summary>
    /// Updates an existing preset.
    /// </summary>
    void UpdatePreset(DeploymentPreset preset);

    /// <summary>
    /// Updates an existing process.
    /// </summary>
    void UpdateProcess(DeploymentProcess process);

    bool DeletePreset(string presetId);
    bool DeleteProcess(string processId);
}
