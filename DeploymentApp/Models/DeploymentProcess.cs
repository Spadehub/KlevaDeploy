namespace DeploymentApp.Models;

public enum ProcessKind { Installer, PowerShellScript, BatchScript, RegistryFile, ConfigAction }

public class DeploymentProcess
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProcessKind Kind { get; set; }
    /// <summary>Path to installer/script relative to AppContext.BaseDirectory, or empty for ConfigAction.</summary>
    public string RelativePath { get; set; } = string.Empty;
    /// <summary>Arguments passed to the process. Use {LICENSE_KEY} as placeholder.</summary>
    public string Arguments { get; set; } = string.Empty;
    /// <summary>URL to download/update this process's file (requires auth if RequiresAuth=true).</summary>
    public string DownloadUrl { get; set; } = string.Empty;
    public bool RequiresAuth { get; set; }
    public bool RequiresLicense { get; set; }
    public string LicenseExcelColumn { get; set; } = string.Empty;
    /// <summary>If true, the process is selected/enabled by default when a preset is chosen.</summary>
    public bool EnabledByDefault { get; set; } = true;
    /// <summary>If true, this process cannot be deselected (required dependency).</summary>
    public bool IsRequired { get; set; }
    /// <summary>IDs of processes that must run before this one (within the merged queue).</summary>
    public List<string> DependsOn { get; set; } = new();
}
