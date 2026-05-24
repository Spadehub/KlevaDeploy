namespace KlevaDeploy.Services.Interfaces;

public interface IUpdateService
{
    Task CheckAndUpdateInstallersAsync(IReadOnlyList<KlevaDeploy.Models.DeploymentProcess> processes, CancellationToken ct = default);
    Task UpdateSingleInstallerAsync(KlevaDeploy.Models.DeploymentProcess process, CancellationToken ct = default);
    Task RedownloadSingleInstallerAsync(KlevaDeploy.Models.DeploymentProcess process, CancellationToken ct = default);
}
