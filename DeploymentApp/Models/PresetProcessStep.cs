namespace DeploymentApp.Models;

public class PresetProcessStep
{
    /// <summary>References DeploymentProcess.Id</summary>
    public string ProcessId { get; set; } = string.Empty;
    /// <summary>Execution order within this preset (lower = runs first).</summary>
    public int Order { get; set; }
    /// <summary>Override the default enabled state for this preset.</summary>
    public bool? EnabledOverride { get; set; }
}
