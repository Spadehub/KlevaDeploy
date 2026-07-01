using KlevaDeploy.Models;

namespace KlevaDeploy.Services.Interfaces;

public interface IAppUpdateService
{
    Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    Task<string?> DownloadUpdateAsync(AppUpdateInfo info, CancellationToken ct = default);
    string? LaunchUpdater(string downloadedUpdatePath);
}

