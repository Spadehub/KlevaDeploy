namespace KlevaDeploy.Models;

public sealed record AppUpdateInfo(
    string Version,
    string DownloadUrl,
    string AssetName);

