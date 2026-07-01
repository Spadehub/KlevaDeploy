namespace KlevaDeploy.Models;

public sealed record AppUpdateInfo(
    string Version,
    string DownloadUrl,
    string AssetName,
    string ReleaseName,
    string ReleaseNotes,
    string ReleasePageUrl,
    bool IsPrerelease,
    DateTimeOffset? PublishedAtUtc,
    long? AssetSizeBytes);

