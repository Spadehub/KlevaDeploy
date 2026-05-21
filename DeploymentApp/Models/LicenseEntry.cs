namespace DeploymentApp.Models;

public class LicenseEntry
{
    public string CustomerName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string LicenseKey { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    /// <summary>Raw row data for any extra columns not explicitly mapped.</summary>
    public Dictionary<string, string> ExtraFields { get; set; } = new();
}
