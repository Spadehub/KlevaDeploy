namespace DeploymentApp.Models;

public enum ScriptType { PowerShell, Batch, Registry }

public class ConfigScript
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ScriptType Type { get; set; }
    /// <summary>Path to the script file, relative to AppContext.BaseDirectory.</summary>
    public string ScriptRelativePath { get; set; } = string.Empty;
    public bool RequiresAuth { get; set; }
}
