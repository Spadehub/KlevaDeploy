using DeploymentApp.Models;

namespace DeploymentApp.Services.Interfaces;

public interface ILicenseScraperService
{
    /// <summary>
    /// Downloads the Excel license sheet from the Passepartout portal (requires auth),
    /// parses it with ClosedXML, and returns all license entries.
    /// </summary>
    Task<IReadOnlyList<LicenseEntry>> FetchLicensesAsync(CancellationToken ct = default);
    /// <summary>Extracts the license key for a specific package from the fetched list.</summary>
    string? ExtractLicenseKey(IReadOnlyList<LicenseEntry> licenses, string productName, string customerName);
}
