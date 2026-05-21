using DeploymentApp.Models;

namespace DeploymentApp.Services.Interfaces;

public interface IInstallerService
{
    Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync();
    Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync();
    /// <summary>
    /// Given selected presets, returns the merged, deduplicated, ordered list of process steps.
    /// If a process appears in multiple presets, use the lowest Order value.
    /// </summary>
    IReadOnlyList<(DeploymentProcess Process, int Order)> BuildExecutionQueue(
        IEnumerable<DeploymentPreset> selectedPresets,
        IReadOnlyList<DeploymentProcess> allProcesses);
    string ResolveProcessPath(DeploymentProcess process);
}
