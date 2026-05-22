namespace KlevaDeploy.Models;

public class SoftwarePackage
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    /// <summary>Path to the local fallback installer, relative to AppContext.BaseDirectory.</summary>
    public string LocalInstallerRelativePath { get; set; } = string.Empty;
    /// <summary>URL to download/update the installer from (requires auth).</summary>
    public string DownloadUrl { get; set; } = string.Empty;
    /// <summary>Silent install arguments passed to the installer process.</summary>
    public string SilentArgs { get; set; } = string.Empty;
    /// <summary>If true, a Passepartout login is required before install/download.</summary>
    public bool RequiresAuth { get; set; }
    /// <summary>If true, a license key must be fetched from the Excel sheet before install.</summary>
    public bool RequiresLicense { get; set; }
    /// <summary>Column name in the Excel sheet that contains the license key for this package.</summary>
    public string LicenseExcelColumn { get; set; } = string.Empty;

    /// <summary>
    /// The individual processes/features that make up this installation preset.
    /// The first item with Kind == MainInstall represents the primary software installer.
    /// </summary>
    public List<PackageFeature> Features { get; set; } = new();
}
