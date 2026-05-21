namespace DeploymentApp.Models;

public class DeploymentPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>Emoji or short icon hint for the UI.</summary>
    public string Icon { get; set; } = "📦";
    public List<PresetProcessStep> Steps { get; set; } = new();
}
