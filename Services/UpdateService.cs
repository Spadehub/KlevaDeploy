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

            if (process.RequiresAuth && !IsAuthenticatedForProcess(process))
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
        if (process.RequiresAuth && !IsAuthenticatedForProcess(process))
        {
            _log.Warning($"Skipping installer update for '{process.Name}': auth required but not logged in.");
            return;
        }

        var storageDir = GetStorageDir();
        var state = InstallerUpdateState.Load(storageDir);
        await UpdateSingleInstallerInternalAsync(process, state, storageDir, forceDownload: false, ct);
        state.Save(storageDir);
    }

    public async Task RedownloadSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default)
    {
        if (process.RequiresAuth && !IsAuthenticatedForProcess(process))
        {
            _log.Warning($"Skipping installer redownload for '{process.Name}': auth required but not logged in.");
            return;
        }

        var storageDir = GetStorageDir();
        var state = InstallerUpdateState.Load(storageDir);
        await UpdateSingleInstallerInternalAsync(process, state, storageDir, forceDownload: true, ct);
        state.Save(storageDir);
    }

    public bool IsStaticWebInstallerCachedForUrl(DeploymentProcess process)
    {
        if (process.Kind != ProcessKind.Installer) return false;
        if (process.InstallerSourceMode != InstallerSourceMode.StaticWeb) return false;
        if (string.IsNullOrWhiteSpace(process.DownloadUrl)) return false;
        if (string.IsNullOrWhiteSpace(process.RelativePath)) return false;

        var localPath = Path.Combine(AppContext.BaseDirectory, process.RelativePath);
        if (!File.Exists(localPath)) return false;

        var storageDir = GetStorageDir();
        var state = InstallerUpdateState.Load(storageDir);
        if (!state.Entries.TryGetValue(process.Id, out var entry)) return false;

        var expected = NormalizeAbsoluteUrl(process.DownloadUrl);
        var actual = NormalizeAbsoluteUrl(entry.LastDownloadedFromUrl);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
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
            if (process.InstallerSourceMode == InstallerSourceMode.DynamicWeb)
            {
                var template = !string.IsNullOrWhiteSpace(process.DownloadSelectedFileTemplate)
                    ? process.DownloadSelectedFileTemplate
                    : process.DownloadSelectedFileName;

                _log.Warning(
                    $"Could not resolve download URL for '{process.Name}'. " +
                    $"baseFolder='{process.DownloadBaseFolderUrl}' template='{template}' " +
                    $"useLatest={process.DownloadUseLatestVersion} version='{process.DownloadVersionFolderName}' " +
                    $"pickLatestByName={process.DownloadPickLatestFolderByName} requiresAuth={process.RequiresAuth} isAuthenticated={_authService.IsAuthenticated}");
            }
            else
            {
                _log.Warning(
                    $"Could not resolve download URL for '{process.Name}'. " +
                    $"downloadUrl='{process.DownloadUrl}' requiresAuth={process.RequiresAuth} isAuthenticated={_authService.IsAuthenticated}");
            }
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

        var prevDownloadedFromUrl = entry.LastDownloadedFromUrl;
        var prevResolvedUrl = entry.LastResolvedDownloadUrl;

        entry.LastCheckedUtc = DateTimeOffset.UtcNow;
        entry.LastResolvedDownloadUrl = remoteUrl;

        var fileExists = File.Exists(localPath);
        var normalizedPrevDownloadedFromUrl = NormalizeAbsoluteUrl(prevDownloadedFromUrl);
        var normalizedRemoteUrl = NormalizeAbsoluteUrl(remoteUrl);

        var selectionChanged =
            fileExists &&
            !string.IsNullOrWhiteSpace(normalizedPrevDownloadedFromUrl) &&
            !string.Equals(normalizedPrevDownloadedFromUrl, normalizedRemoteUrl, StringComparison.OrdinalIgnoreCase);

        var resolvedChangedWithoutHistory =
            fileExists &&
            string.IsNullOrWhiteSpace(normalizedPrevDownloadedFromUrl) &&
            !string.IsNullOrWhiteSpace(prevResolvedUrl) &&
            !string.Equals(NormalizeAbsoluteUrl(prevResolvedUrl), normalizedRemoteUrl, StringComparison.OrdinalIgnoreCase);

        var isAssociatedFallback = fileExists &&
                                   !string.IsNullOrWhiteSpace(normalizedPrevDownloadedFromUrl) &&
                                   string.Equals(normalizedPrevDownloadedFromUrl, normalizedRemoteUrl, StringComparison.OrdinalIgnoreCase);

        var needDownload = forceDownload || !fileExists || selectionChanged || resolvedChangedWithoutHistory;

        if (!needDownload)
        {
            if (process.InstallerSourceMode == InstallerSourceMode.DynamicWeb)
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
            else if (process.InstallerSourceMode == InstallerSourceMode.StaticWeb)
            {
                // StaticWeb behavior:
                // - If the cached file exists and it is already associated with the same URL, do nothing.
                // - Otherwise, download to (re)associate the cache with the current URL.
                if (fileExists && !forceDownload && isAssociatedFallback)
                    return;

                needDownload = forceDownload || !fileExists || selectionChanged || string.IsNullOrWhiteSpace(normalizedPrevDownloadedFromUrl);
            }
        }

        _log.Info($"Downloading update for '{process.Name}'...");

        var tempPath = localPath + ".download";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        var installerDir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrWhiteSpace(installerDir) && Directory.Exists(installerDir))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(installerDir))
                {
                    if (string.Equals(path, localPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(path, tempPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!IsInstallerArtifact(path)) continue;
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Could not clean old cached installers for '{process.Name}': {ex.GetType().Name}: {ex.Message}");
            }
        }

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
        entry.LastDownloadedFromUrl = remoteUrl;

        _log.Info($"'{process.Name}' installer updated ({downloadResult.BytesWritten} bytes).");
    }

    private static bool IsInstallerArtifact(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".msi" or ".zip" or ".download" or ".tmp";
    }

    private bool IsAuthenticatedForProcess(DeploymentProcess process)
    {
        var url = process.InstallerSourceMode == InstallerSourceMode.DynamicWeb
            ? process.DownloadBaseFolderUrl
            : process.DownloadUrl;

        return !string.IsNullOrWhiteSpace(url) && _authService.IsAuthenticatedForUrl(url);
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
        try
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

            return NormalizeAbsoluteUrl(process.DownloadUrl);
        }
        catch (Exception ex)
        {
            _log.Error($"ResolveRemoteDownloadUrlAsync failed for '{process.Name}'", ex);
            return null;
        }
    }

    private static string NormalizeAbsoluteUrl(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        if (Uri.TryCreate(raw, UriKind.Absolute, out _)) return raw;
        if (!raw.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate("https://" + raw, UriKind.Absolute, out _))
        {
            return "https://" + raw;
        }

        return raw;
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
        if (response is null)
        {
            _log.Warning($"HEAD failed for '{url}' (no response).");
            return HeadCheckResult.Unknown;
        }

        if (response.StatusCode == HttpStatusCode.NotModified)
            return HeadCheckResult.NotModified;

        if (!response.IsSuccessStatusCode)
        {
            _log.Warning($"HEAD failed for '{url}' (HTTP {(int)response.StatusCode} {response.StatusCode}).");
            return HeadCheckResult.Unknown;
        }

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
        if (response is null)
        {
            _log.Warning($"GET failed for '{url}' (no response).");
            return new DownloadResult(false, 0);
        }

        if (!response.IsSuccessStatusCode)
        {
            _log.Warning($"GET failed for '{url}' (HTTP {(int)response.StatusCode} {response.StatusCode}).");
            return new DownloadResult(false, 0);
        }

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
