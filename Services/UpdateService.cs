using System.IO;
using System.Net;
using System.Net.Http;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly IDownloadDirectoryListingService _directoryListing;
    private readonly ILogService _log;

    public UpdateService(HttpClient httpClient, IAuthService authService, IDownloadDirectoryListingService directoryListing, ILogService log)
    {
        _httpClient = httpClient;
        _authService = authService;
        _directoryListing = directoryListing;
        _log = log;
    }

    public async Task CheckAndUpdateInstallersAsync(IReadOnlyList<DeploymentProcess> processes, CancellationToken ct = default)
    {
        if (!await IsInternetAvailableAsync(ct))
        {
            _log.Info("No internet connection. Using local fallback installers.");
            return;
        }

        _log.Info("Internet available. Checking for installer updates...");

        var storageDir = GetStorageDir();
        var state = InstallerUpdateState.Load(storageDir);

        foreach (var process in processes.Where(p => p.Kind == ProcessKind.Installer))
        {
            if (!HasAnyDownloadSource(process)) continue;

            if (process.RequiresAuth && !_authService.IsAuthenticated)
            {
                _log.Warning($"Skipping update for '{process.Name}': auth required but not logged in.");
                continue;
            }

            try
            {
                await UpdateSingleInstallerInternalAsync(process, state, storageDir, forceDownload: false, ct);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to update '{process.Name}'", ex);
            }
        }

        state.Save(storageDir);
    }

    public async Task UpdateSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default)
    {
        var storageDir = GetStorageDir();
        var state = InstallerUpdateState.Load(storageDir);
        await UpdateSingleInstallerInternalAsync(process, state, storageDir, forceDownload: false, ct);
        state.Save(storageDir);
    }

    public async Task RedownloadSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default)
    {
        var storageDir = GetStorageDir();
        var state = InstallerUpdateState.Load(storageDir);
        await UpdateSingleInstallerInternalAsync(process, state, storageDir, forceDownload: true, ct);
        state.Save(storageDir);
    }

    private async Task UpdateSingleInstallerInternalAsync(
        DeploymentProcess process,
        InstallerUpdateState state,
        string storageDir,
        bool forceDownload,
        CancellationToken ct)
    {
        if (process.Kind != ProcessKind.Installer) return;
        if (!HasAnyDownloadSource(process)) return;
        if (process.InstallerSourceMode == InstallerSourceMode.StaticLocal) return;

        var remoteUrl = await ResolveRemoteDownloadUrlAsync(process, ct);
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            _log.Warning($"Could not resolve download URL for '{process.Name}'.");
            return;
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
        var dir = Path.GetDirectoryName(localPath);
        if (dir is not null) Directory.CreateDirectory(dir);

        if (!state.Entries.TryGetValue(process.Id, out var entry))
        {
            entry = new InstallerUpdateStateEntry();
            state.Entries[process.Id] = entry;
        }

        entry.LastCheckedUtc = DateTimeOffset.UtcNow;
        entry.LastResolvedDownloadUrl = remoteUrl;

        var needDownload = forceDownload || !File.Exists(localPath);
        if (!needDownload) return;

        if (!forceDownload && process.InstallerSourceMode != InstallerSourceMode.StaticWeb)
        {
            var head = await TryHeadAsync(remoteUrl, entry, ct);
            if (head == HeadCheckResult.NotModified)
            {
                _log.Info($"'{process.Name}' is up to date.");
                return;
            }

            needDownload = head == HeadCheckResult.Modified;
            if (!needDownload) return;
        }

        _log.Info($"Downloading update for '{process.Name}'...");

        var tempPath = localPath + ".download";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        var downloadResult = await DownloadToFileAsync(remoteUrl, tempPath, entry, ct);
        if (!downloadResult.Success)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            _log.Warning($"Download failed for '{process.Name}'.");
            return;
        }

        File.Move(tempPath, localPath, overwrite: true);

        entry.LastDownloadedUtc = DateTimeOffset.UtcNow;
        entry.LastDownloadedBytes = downloadResult.BytesWritten;

        _log.Info($"'{process.Name}' installer updated ({downloadResult.BytesWritten} bytes).");
    }

    private bool HasAnyDownloadSource(DeploymentProcess process) =>
        process.InstallerSourceMode switch
        {
            InstallerSourceMode.StaticWeb => !string.IsNullOrWhiteSpace(process.DownloadUrl),
            InstallerSourceMode.DynamicWeb =>
                !string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl) &&
                (!string.IsNullOrWhiteSpace(process.DownloadSelectedFileTemplate) || !string.IsNullOrWhiteSpace(process.DownloadSelectedFileName)),
            _ => false
        };

    private async Task<string?> ResolveRemoteDownloadUrlAsync(DeploymentProcess process, CancellationToken ct)
    {
        if (process.InstallerSourceMode == InstallerSourceMode.DynamicWeb &&
            !string.IsNullOrWhiteSpace(process.DownloadBaseFolderUrl))
        {
            var template = !string.IsNullOrWhiteSpace(process.DownloadSelectedFileTemplate)
                ? process.DownloadSelectedFileTemplate
                : process.DownloadSelectedFileName;

            var versionFolderName = (!process.DownloadUseLatestVersion && !string.IsNullOrWhiteSpace(process.DownloadVersionFolderName))
                ? process.DownloadVersionFolderName
                : null;

            return await _directoryListing.ResolveDownloadUrlAsync(
                process.DownloadBaseFolderUrl,
                pickLatestFolderByName: process.DownloadPickLatestFolderByName,
                selectedFileTemplate: template,
                versionFolderName: versionFolderName,
                ct: ct);
        }

        return process.DownloadUrl;
    }

    private enum HeadCheckResult { Unknown, NotModified, Modified }

    private async Task<HeadCheckResult> TryHeadAsync(string url, InstallerUpdateStateEntry entry, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);

        if (!string.IsNullOrWhiteSpace(entry.LastKnownEtag))
            request.Headers.TryAddWithoutValidation("If-None-Match", entry.LastKnownEtag);

        if (entry.LastKnownLastModifiedUtc is not null)
            request.Headers.IfModifiedSince = entry.LastKnownLastModifiedUtc.Value.UtcDateTime;

        using var response = await SendWithRedirectsAsync(request, ct);
        if (response is null) return HeadCheckResult.Unknown;

        if (response.StatusCode == HttpStatusCode.NotModified)
            return HeadCheckResult.NotModified;

        if (!response.IsSuccessStatusCode)
            return HeadCheckResult.Unknown;

        var prevEtag = entry.LastKnownEtag;
        var prevLastModified = entry.LastKnownLastModifiedUtc;
        var etag = response.Headers.ETag?.Tag;
        if (!string.IsNullOrWhiteSpace(etag))
            entry.LastKnownEtag = etag;

        if (response.Content.Headers.LastModified is not null)
            entry.LastKnownLastModifiedUtc = response.Content.Headers.LastModified.Value.ToUniversalTime();

        if (!string.IsNullOrWhiteSpace(etag) && string.Equals(etag, prevEtag, StringComparison.OrdinalIgnoreCase))
            return HeadCheckResult.NotModified;

        if (string.IsNullOrWhiteSpace(etag) &&
            entry.LastKnownLastModifiedUtc is not null &&
            prevLastModified is not null &&
            entry.LastKnownLastModifiedUtc.Value.UtcDateTime == prevLastModified.Value.UtcDateTime)
        {
            return HeadCheckResult.NotModified;
        }

        return HeadCheckResult.Modified;
    }

    private sealed record DownloadResult(bool Success, long BytesWritten);

    private async Task<DownloadResult> DownloadToFileAsync(string url, string destPath, InstallerUpdateStateEntry entry, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithRedirectsAsync(request, ct);
        if (response is null || !response.IsSuccessStatusCode)
            return new DownloadResult(false, 0);

        var etag = response.Headers.ETag?.Tag;
        if (!string.IsNullOrWhiteSpace(etag))
            entry.LastKnownEtag = etag;

        if (response.Content.Headers.LastModified is not null)
            entry.LastKnownLastModifiedUtc = response.Content.Headers.LastModified.Value.ToUniversalTime();

        await using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var inStream = await response.Content.ReadAsStreamAsync(ct);
        await inStream.CopyToAsync(outStream, ct);
        return new DownloadResult(true, outStream.Length);
    }

    private async Task<HttpResponseMessage?> SendWithRedirectsAsync(HttpRequestMessage request, CancellationToken ct)
    {
        const int maxRedirects = 10;
        HttpResponseMessage? response = null;
        var currentRequest = request;

        for (int i = 0; i < maxRedirects; i++)
        {
            response?.Dispose();
            response = await _httpClient.SendAsync(currentRequest, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!IsRedirectStatusCode(response.StatusCode))
                return response;

            var location = response.Headers.Location;
            if (location is null) return response;

            var nextUri = location.IsAbsoluteUri
                ? location
                : new Uri(currentRequest.RequestUri ?? new Uri("https://download.passepartout.cloud/"), location);

            currentRequest.Dispose();
            currentRequest = new HttpRequestMessage(HttpMethod.Get, nextUri);
        }

        return response;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string GetStorageDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    private async Task<bool> IsInternetAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _httpClient.GetAsync("https://www.google.com", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
