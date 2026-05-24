namespace KlevaDeploy.Services.Interfaces;

public interface IDownloadDirectoryListingService
{
    Task<LatestFolderExeListing?> GetLatestFolderExeListingAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListSubfoldersAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        CancellationToken ct = default);

    Task<LatestFolderExeListing?> GetFolderExeListingAsync(
        string folderUrl,
        CancellationToken ct = default);

    Task<string?> ResolveDownloadUrlAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        string selectedFileTemplate,
        CancellationToken ct = default);

    Task<string?> ResolveDownloadUrlAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        string selectedFileTemplate,
        string? versionFolderName,
        CancellationToken ct = default);
}

public sealed record LatestFolderExeListing(string FolderName, IReadOnlyList<string> ExeFiles);

