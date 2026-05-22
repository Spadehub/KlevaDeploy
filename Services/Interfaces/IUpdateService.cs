using DeploymentApp.Models;

namespace DeploymentApp.Services.Interfaces;

public interface IUpdateService
{
    /// <summary>
    /// Checks internet connectivity. If online and authenticated,
    /// downloads updated installers and overwrites local fallback copies.
    /// Runs silently in the background.
    /// </summary>
    Task CheckAndUpdateAsync(IReadOnlyList<SoftwarePackage> packages, CancellationToken ct = default);
}
