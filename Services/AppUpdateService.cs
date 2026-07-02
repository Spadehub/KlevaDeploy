using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class AppUpdateService : IAppUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _log;
    private readonly AppUpdateServiceConfig _cfg;

    public AppUpdateService(HttpClient httpClient, ILogService log, IAppConfigService config)
    {
        _httpClient = httpClient;
        _log = log;
        _cfg = config.Config.AppUpdateService;
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var owner = GetSetting("KLEVADEPLOY_GITHUB_OWNER", _cfg.Owner);
        var repoFromEnv = Environment.GetEnvironmentVariable("KLEVADEPLOY_GITHUB_REPO");
        var reposToTry = string.IsNullOrWhiteSpace(repoFromEnv)
            ? new[] { _cfg.Repo, _cfg.LegacyRepo }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray()
            : new[] { repoFromEnv.Trim() };
        var assetName = GetSetting("KLEVADEPLOY_GITHUB_ASSET_NAME", _cfg.AssetName);
        var includePrereleases = GetBoolSetting("KLEVADEPLOY_GITHUB_INCLUDE_PRERELEASES", false);
        var token = Environment.GetEnvironmentVariable(_cfg.TokenEnvVar);
        var currentVersionString = GetCurrentVersionString();
        if (!SemVersion.TryParse(currentVersionString, out var currentVersion))
            return null;

        EnsureDefaultHeaders();

        HttpStatusCode? lastStatus = null;
        foreach (var repo in reposToTry)
        {
            var url = includePrereleases
                ? $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=20"
                : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyGitHubAuth(request, token);
            using var response = await SendWithRedirectsAsync(request, token, ct);
            lastStatus = response?.StatusCode;

            if (response is null || !response.IsSuccessStatusCode)
            {
                if (response?.StatusCode == HttpStatusCode.NotFound)
                    continue;

                _log.Warning($"App update check failed: HTTP {(int)(response?.StatusCode ?? 0)} (owner={owner}, repo={repo})");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var release in EnumerateCandidateReleases(doc.RootElement, includePrereleases))
            {
                var tag = release.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag)) continue;

                var remoteVersionString = tag.Trim().TrimStart('v', 'V');
                if (!SemVersion.TryParse(remoteVersionString, out var remoteVersion)) continue;
                if (remoteVersion.CompareTo(currentVersion) <= 0) continue;

                var info = TryBuildAppUpdateInfo(release, remoteVersion, assetName);
                if (info is not null)
                    return info;
            }
        }

        if (lastStatus == HttpStatusCode.NotFound)
        {
            var hint = string.IsNullOrWhiteSpace(token)
                ? $" If the repo is private, set {_cfg.TokenEnvVar}."
                : string.Empty;
            _log.Warning($"App update check failed: HTTP 404 (owner={owner}, repo={string.Join("/", reposToTry)}). Repo missing/private or no GitHub Releases yet.{hint}");
        }

        return null;
    }

    public async Task<string?> DownloadUpdateAsync(AppUpdateInfo info, CancellationToken ct = default)
    {
        EnsureDefaultHeaders();
        var token = Environment.GetEnvironmentVariable(_cfg.TokenEnvVar);

        var storageDir = GetStorageDir();
        var dir = Path.Combine(storageDir, "app_updates");
        Directory.CreateDirectory(dir);

        var fileName = $"KlevaDeploy-{info.Version}.exe";
        var destPath = Path.Combine(dir, fileName);
        var tempPath = destPath + ".download";

        if (File.Exists(destPath))
        {
            if (info.AssetSizeBytes is null)
                return destPath;

            var existingLength = new FileInfo(destPath).Length;
            if (existingLength == info.AssetSizeBytes.Value)
                return destPath;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        ApplyGitHubAuth(request, token);
        using var response = await SendWithRedirectsAsync(request, token, ct);
        if (response is null || !response.IsSuccessStatusCode)
        {
            _log.Warning($"App update download failed: HTTP {(int)(response?.StatusCode ?? 0)}");
            return null;
        }

        if (File.Exists(tempPath)) File.Delete(tempPath);

        await using var outStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var inStream = await response.Content.ReadAsStreamAsync(ct);
        await inStream.CopyToAsync(outStream, ct);

        File.Move(tempPath, destPath, overwrite: true);
        return destPath;
    }

    public string? LaunchUpdater(string downloadedUpdatePath)
    {
        if (string.IsNullOrWhiteSpace(downloadedUpdatePath) || !File.Exists(downloadedUpdatePath))
            return "Downloaded update executable not found.";

        var target = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(target))
            return "Current application path is not available.";

        try
        {
            var pid = Environment.ProcessId;
            Process.Start(new ProcessStartInfo(downloadedUpdatePath, $"--apply-update --pid {pid} --target \"{target}\"")
            {
                UseShellExecute = true
            });
            return null;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to launch app updater", ex);
            return ex.Message;
        }
    }

    private void EnsureDefaultHeaders()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KlevaDeploy");
    }

    private async Task<HttpResponseMessage?> SendWithRedirectsAsync(HttpRequestMessage request, string? token, CancellationToken ct)
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
                : new Uri(currentRequest.RequestUri ?? new Uri("https://api.github.com/"), location);

            currentRequest.Dispose();
            currentRequest = new HttpRequestMessage(HttpMethod.Get, nextUri);
            ApplyGitHubAuth(currentRequest, token);
        }

        return response;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static void ApplyGitHubAuth(HttpRequestMessage request, string? token)
    {
        if (request.RequestUri is null) return;
        if (!IsGitHubHost(request.RequestUri.Host)) return;

        request.Headers.TryAddWithoutValidation("User-Agent", "KlevaDeploy");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        if (string.IsNullOrWhiteSpace(token)) return;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
    }

    private static bool IsGitHubHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        return host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
               || host.EndsWith("githubassets.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetSetting(string envVar, string fallback)
    {
        var v = Environment.GetEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }

    private static bool GetBoolSetting(string envVar, bool fallback)
    {
        var v = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        return bool.TryParse(v, out var b) ? b : fallback;
    }

    private static string GetCurrentVersionString()
    {
        var overridden = Environment.GetEnvironmentVariable("KLEVADEPLOY_APP_VERSION_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden.Trim();

        var asm = Assembly.GetExecutingAssembly();
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational;

        var v = asm.GetName().Version;
        if (v is null)
            return "0.0.0";

        return v.Revision >= 0
            ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
            : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static IEnumerable<JsonElement> EnumerateCandidateReleases(JsonElement root, bool includePrereleases)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (ShouldConsiderRelease(root, includePrereleases))
                yield return root;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var release in root.EnumerateArray())
        {
            if (!ShouldConsiderRelease(release, includePrereleases)) continue;
            yield return release;
        }
    }

    private static bool ShouldConsiderRelease(JsonElement release, bool includePrereleases)
    {
        var isDraft = release.TryGetProperty("draft", out var draftEl) && draftEl.ValueKind == JsonValueKind.True;
        if (isDraft) return false;

        var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseEl) && prereleaseEl.ValueKind == JsonValueKind.True;
        if (!includePrereleases && isPrerelease) return false;

        return true;
    }

    private static AppUpdateInfo? TryBuildAppUpdateInfo(JsonElement release, SemVersion remoteVersion, string assetName)
    {
        if (!release.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assetsEl.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
            if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) continue;

            var dl = asset.TryGetProperty("browser_download_url", out var dlEl) ? dlEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(dl)) return null;

            var releaseName = release.TryGetProperty("name", out var releaseNameEl) ? releaseNameEl.GetString() : null;
            var releaseNotes = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;
            var releasePageUrl = release.TryGetProperty("html_url", out var htmlEl) ? htmlEl.GetString() : null;
            var isPrerelease = release.TryGetProperty("prerelease", out var prereleaseEl) && prereleaseEl.ValueKind == JsonValueKind.True;
            var publishedAtUtc = TryGetDateTimeOffset(release, "published_at") ?? TryGetDateTimeOffset(release, "created_at");
            long? assetSizeBytes = null;
            if (asset.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.Number && sizeEl.TryGetInt64(out var size))
                assetSizeBytes = size;

            return new AppUpdateInfo(
                remoteVersion.ToString(),
                dl,
                name ?? assetName,
                string.IsNullOrWhiteSpace(releaseName) ? $"v{remoteVersion}" : releaseName.Trim(),
                releaseNotes?.Trim() ?? string.Empty,
                releasePageUrl?.Trim() ?? string.Empty,
                isPrerelease,
                publishedAtUtc,
                assetSizeBytes);
        }

        return null;
    }

    private static string GetStorageDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    private readonly record struct SemVersion(
        int Major,
        int Minor,
        int Patch,
        int Revision,
        string[] PreReleaseIdentifiers) : IComparable<SemVersion>
    {
        public static bool TryParse(string? s, out SemVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var input = s.Trim();
            var plusIndex = input.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex >= 0) input = input[..plusIndex];

            var dashIndex = input.IndexOf('-', StringComparison.Ordinal);
            var core = dashIndex >= 0 ? input[..dashIndex] : input;
            var prerelease = dashIndex >= 0 ? input[(dashIndex + 1)..] : string.Empty;

            var parts = core.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is < 3 or > 4) return false;

            if (!int.TryParse(parts[0], out var major)) return false;
            if (!int.TryParse(parts[1], out var minor)) return false;
            if (!int.TryParse(parts[2], out var patch)) return false;
            var revision = 0;
            if (parts.Length == 4 && !int.TryParse(parts[3], out revision)) return false;

            var ids = string.IsNullOrWhiteSpace(prerelease)
                ? Array.Empty<string>()
                : prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            version = new SemVersion(major, minor, patch, revision, ids);
            return true;
        }

        public int CompareTo(SemVersion other)
        {
            var c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            c = Revision.CompareTo(other.Revision);
            if (c != 0) return c;

            var thisHasPre = PreReleaseIdentifiers.Length > 0;
            var otherHasPre = other.PreReleaseIdentifiers.Length > 0;

            if (!thisHasPre && !otherHasPre) return 0;
            if (!thisHasPre) return 1;
            if (!otherHasPre) return -1;

            var min = Math.Min(PreReleaseIdentifiers.Length, other.PreReleaseIdentifiers.Length);
            for (var i = 0; i < min; i++)
            {
                var a = PreReleaseIdentifiers[i];
                var b = other.PreReleaseIdentifiers[i];

                var aIsNum = int.TryParse(a, out var aNum);
                var bIsNum = int.TryParse(b, out var bNum);

                if (aIsNum && bIsNum)
                {
                    c = aNum.CompareTo(bNum);
                    if (c != 0) return c;
                    continue;
                }

                if (aIsNum && !bIsNum) return -1;
                if (!aIsNum && bIsNum) return 1;

                c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
            }

            return PreReleaseIdentifiers.Length.CompareTo(other.PreReleaseIdentifiers.Length);
        }

        public override string ToString()
        {
            var core = Revision == 0
                ? $"{Major}.{Minor}.{Patch}"
                : $"{Major}.{Minor}.{Patch}.{Revision}";

            if (PreReleaseIdentifiers.Length == 0)
                return core;
            return $"{core}-{string.Join('.', PreReleaseIdentifiers)}";
        }
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var valueElement))
            return null;
        if (valueElement.ValueKind != JsonValueKind.String)
            return null;

        var raw = valueElement.GetString();
        return DateTimeOffset.TryParse(raw, out var value) ? value : null;
    }
}

