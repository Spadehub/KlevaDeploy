namespace KlevaDeploy.Models;

public enum ProcessKind { Installer, PowerShellScript, BatchScript, BashScript, RegistryFile, ConfigAction }

public enum InstallerSourceMode
{
    StaticLocal,
    StaticWeb,
    DynamicWeb
}

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
    public List<ArgumentInputDefinition> ArgumentInputs { get; set; } = new();
    /// <summary>URL to download/update this process's file (requires auth if RequiresAuth=true).</summary>
    public string DownloadUrl { get; set; } = string.Empty;
    public string DownloadBaseFolderUrl { get; set; } = string.Empty;
    public string DownloadSelectedFileName { get; set; } = string.Empty;
    public string DownloadSelectedFileTemplate { get; set; } = string.Empty;
    public bool DownloadPickLatestFolderByName { get; set; }
    public InstallerSourceMode InstallerSourceMode { get; set; } = InstallerSourceMode.StaticLocal;
    public bool DownloadUseLatestVersion { get; set; } = true;
    public string DownloadVersionFolderName { get; set; } = string.Empty;
    public bool RequiresAuth { get; set; }
    public string? PortalId { get; set; }
    public bool RequiresLicense { get; set; }
    public string LicenseExcelColumn { get; set; } = string.Empty;
    /// <summary>If true, the process is selected/enabled by default when a preset is chosen.</summary>
    public bool EnabledByDefault { get; set; } = true;
    /// <summary>If true, this process cannot be deselected (required dependency).</summary>
    public bool IsRequired { get; set; }
    /// <summary>IDs of processes that must run before this one (within the merged queue).</summary>
    public List<string> DependsOn { get; set; } = new();
    /// <summary>If true, run the process with administrator privileges.</summary>
    public bool RunAsAdmin { get; set; }
    /// <summary>If true, this process requires an internet connection.</summary>
    public bool RequiresInternet { get; set; }
    /// <summary>For PowerShell/Batch scripts: inline script content (alternative to RelativePath).</summary>
    public string ScriptContent { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    /// <summary>Icon key from Icons.xaml (e.g., "IconPackage", "IconScript").</summary>
    public string IconKey { get; set; } = "IconPackage";
    public string Icon { get; set; } = "📦";
    public string? CustomIconLightPath { get; set; }
    public string? CustomIconDarkPath { get; set; }
    /// <summary>If true, this process was created by the user (not from presets).</summary>
    public bool IsUserCreated { get; set; }

    public List<DeploymentSubProcess> SubProcesses { get; set; } = new();
}

public sealed class ArgumentInputDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
    public bool IsRequired { get; set; } = true;
}

public sealed class DeploymentSubProcess
{
    public string Name { get; set; } = string.Empty;
    public DeploymentProcess? Process { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool? RunAsAdmin { get; set; }
}
