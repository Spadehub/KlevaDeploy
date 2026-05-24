using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class AppUpdateService : IAppUpdateService
{
    private const string DefaultOwner = "Spadehub";
    private const string DefaultRepo = "KlevaDeploy";
    private const string LegacyRepo = "InstallerIT";
    private const string DefaultAssetName = "KlevaDeploy.exe";

    private readonly HttpClient _httpClient;
    private readonly ILogService _log;

    public AppUpdateService(HttpClient httpClient, ILogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var owner = GetSetting("KLEVADEPLOY_GITHUB_OWNER", DefaultOwner);
        var repoFromEnv = Environment.GetEnvironmentVariable("KLEVADEPLOY_GITHUB_REPO");
        var reposToTry = string.IsNullOrWhiteSpace(repoFromEnv)
            ? new[] { DefaultRepo, LegacyRepo }
            : new[] { repoFromEnv.Trim() };
        var assetName = GetSetting("KLEVADEPLOY_GITHUB_ASSET_NAME", DefaultAssetName);
        var includePrereleases = GetBoolSetting("KLEVADEPLOY_GITHUB_INCLUDE_PRERELEASES", false);

        EnsureDefaultHeaders();

        HttpStatusCode? lastStatus = null;
        foreach (var repo in reposToTry)
        {
            var url = includePrereleases
                ? $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=20"
                : $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendWithRedirectsAsync(request, ct);
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

            var release = TryGetReleaseElement(doc.RootElement, includePrereleases);
            if (release is null) return null;

            var tag = release.Value.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var remoteVersionString = tag.Trim().TrimStart('v', 'V');

            if (!SemVersion.TryParse(remoteVersionString, out var remoteVersion))
                return null;

            var currentVersionString = GetCurrentVersionString();
            if (!SemVersion.TryParse(currentVersionString, out var currentVersion))
                return null;

            if (remoteVersion.CompareTo(currentVersion) <= 0)
                return null;

            if (!release.Value.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) continue;

                var dl = asset.TryGetProperty("browser_download_url", out var dlEl) ? dlEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(dl)) return null;

                return new AppUpdateInfo(remoteVersion.ToString(), dl, name ?? assetName);
            }
        }

        if (lastStatus == HttpStatusCode.NotFound)
        {
            _log.Warning($"App update check failed: HTTP 404 (owner={owner}, repo={string.Join("/", reposToTry)}). Repo missing/private or no GitHub Releases yet.");
        }

        return null;
    }

    public async Task<string?> DownloadUpdateAsync(AppUpdateInfo info, CancellationToken ct = default)
    {
        EnsureDefaultHeaders();

        var storageDir = GetStorageDir();
        var dir = Path.Combine(storageDir, "app_updates");
        Directory.CreateDirectory(dir);

        var fileName = $"KlevaDeploy-{info.Version}.exe";
        var destPath = Path.Combine(dir, fileName);
        var tempPath = destPath + ".download";

        using var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        using var response = await SendWithRedirectsAsync(request, ct);
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

    private void EnsureDefaultHeaders()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "KlevaDeploy");
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
                : new Uri(currentRequest.RequestUri ?? new Uri("https://api.github.com/"), location);

            currentRequest.Dispose();
            currentRequest = new HttpRequestMessage(HttpMethod.Get, nextUri);
        }

        return response;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

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
        var asm = Assembly.GetExecutingAssembly();
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational;

        var v = asm.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static JsonElement? TryGetReleaseElement(JsonElement root, bool includePrereleases)
    {
        if (!includePrereleases)
            return root;

        if (root.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var r in root.EnumerateArray())
        {
            var isDraft = r.TryGetProperty("draft", out var draftEl) && draftEl.ValueKind == JsonValueKind.True;
            if (isDraft) continue;
            return r;
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

            var ids = string.IsNullOrWhiteSpace(prerelease)
                ? Array.Empty<string>()
                : prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            version = new SemVersion(major, minor, patch, ids);
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
            if (PreReleaseIdentifiers.Length == 0)
                return $"{Major}.{Minor}.{Patch}";
            return $"{Major}.{Minor}.{Patch}-{string.Join('.', PreReleaseIdentifiers)}";
        }
    }
}

