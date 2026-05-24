using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using HtmlAgilityPack;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class DownloadDirectoryListingService : IDownloadDirectoryListingService
{
    private static readonly Regex VersionFolderRegex = new(@"^(?<year>\d{4})(?<suffix>[A-Za-z0-9]+)$", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly ILogService _log;

    public DownloadDirectoryListingService(HttpClient httpClient, ILogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    public async Task<LatestFolderExeListing?> GetLatestFolderExeListingAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        CancellationToken ct = default)
    {
        using var debug = new DownloadDebug(_log);

        var latestFolderUrl = await ResolveLatestFolderUrlAsync(baseFolderUrl, pickLatestFolderByName, ct);
        if (latestFolderUrl is null)
        {
            debug.Note($"ResolveLatestFolderUrlAsync returned null for baseFolderUrl='{baseFolderUrl}'");
            debug.FlushToLast();
            return null;
        }

        var html = await GetStringWithRedirectsAsync(latestFolderUrl, ct, debug);
        if (html is null)
        {
            debug.Note($"Failed to fetch listing HTML for latestFolderUrl='{latestFolderUrl}'");
            debug.FlushToLast();
            return null;
        }

        var fileLinks = ExtractLinkTexts(html, latestFolderUrl)
            .Where(l => !l.IsFolder && l.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folderName = latestFolderUrl.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        debug.Note($"latestFolder='{folderName}', exeCount={fileLinks.Count}");

        if (fileLinks.Count == 0)
            debug.FlushToLast();

        return new LatestFolderExeListing(folderName, fileLinks);
    }

    public async Task<IReadOnlyList<string>> ListSubfoldersAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        CancellationToken ct = default)
    {
        using var debug = new DownloadDebug(_log);

        if (!Uri.TryCreate(baseFolderUrl, UriKind.Absolute, out var baseUri))
            return Array.Empty<string>();

        var requestUri = NormalizePassepartoutUrl(baseUri);
        var html = await GetStringWithRedirectsAsync(requestUri, ct, debug);
        if (html is null)
        {
            debug.Note($"Failed to fetch listing HTML for baseFolderUrl='{baseFolderUrl}'");
            debug.FlushToLast();
            return Array.Empty<string>();
        }

        var allLinks = ExtractLinkTexts(html, requestUri);
        var directoryPath = GetDirectoryPath(requestUri);

        var folderLinks = allLinks
            .Where(l => l.IsFolder)
            .Where(l => !string.IsNullOrWhiteSpace(l.Name))
            .Where(l => !string.Equals(GetDirectoryPath(l.Url), directoryPath, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.Url.AbsolutePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            .Where(l => !string.Equals(l.Name, "home", StringComparison.OrdinalIgnoreCase))
            .Where(l => !string.Equals(l.Name, "logout", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Name.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        folderLinks.Sort(pickLatestFolderByName
            ? Comparer<string>.Create(CompareVersionFolderNames)
            : StringComparer.OrdinalIgnoreCase);

        return folderLinks;
    }

    public async Task<LatestFolderExeListing?> GetFolderExeListingAsync(
        string folderUrl,
        CancellationToken ct = default)
    {
        using var debug = new DownloadDebug(_log);

        if (!Uri.TryCreate(folderUrl, UriKind.Absolute, out var folderUri))
            return null;

        var requestUri = NormalizePassepartoutUrl(folderUri);
        var html = await GetStringWithRedirectsAsync(requestUri, ct, debug);
        if (html is null)
        {
            debug.Note($"Failed to fetch listing HTML for folderUrl='{folderUrl}'");
            debug.FlushToLast();
            return null;
        }

        var fileLinks = ExtractLinkTexts(html, requestUri)
            .Where(l => !l.IsFolder && l.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folderName = requestUri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        debug.Note($"folder='{folderName}', exeCount={fileLinks.Count}");
        if (fileLinks.Count == 0)
            debug.FlushToLast();

        return new LatestFolderExeListing(folderName, fileLinks);
    }

    public async Task<string?> ResolveDownloadUrlAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        string selectedFileTemplate,
        CancellationToken ct = default)
    {
        using var debug = new DownloadDebug(_log);

        if (string.IsNullOrWhiteSpace(selectedFileTemplate)) return null;

        var latestFolderUrl = await ResolveLatestFolderUrlAsync(baseFolderUrl, pickLatestFolderByName, ct);
        if (latestFolderUrl is null)
        {
            debug.Note($"ResolveLatestFolderUrlAsync returned null for baseFolderUrl='{baseFolderUrl}'");
            debug.FlushToLast();
            return null;
        }

        var folderName = latestFolderUrl.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
        var selectedFileName = selectedFileTemplate.Replace("{VERSION}", folderName, StringComparison.OrdinalIgnoreCase);

        var html = await GetStringWithRedirectsAsync(latestFolderUrl, ct, debug);
        if (html is null)
        {
            debug.Note($"Failed to fetch listing HTML for latestFolderUrl='{latestFolderUrl}'");
            debug.FlushToLast();
            return null;
        }

        var links = ExtractLinkTexts(html, latestFolderUrl);
        var match = links.FirstOrDefault(l =>
            !l.IsFolder &&
            string.Equals(l.Name, selectedFileName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            debug.Note($"selectedFileName not found: '{selectedFileName}'");
            debug.SaveText("listing-links.txt", SummarizeLinks(links));
            debug.FlushToLast();
            return null;
        }

        debug.Note($"resolvedUrl='{match.Url}'");
        return match.Url.ToString();
    }

    public async Task<string?> ResolveDownloadUrlAsync(
        string baseFolderUrl,
        bool pickLatestFolderByName,
        string selectedFileTemplate,
        string? versionFolderName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(versionFolderName))
        {
            return await ResolveDownloadUrlAsync(baseFolderUrl, pickLatestFolderByName, selectedFileTemplate, ct);
        }

        using var debug = new DownloadDebug(_log);

        if (string.IsNullOrWhiteSpace(selectedFileTemplate)) return null;
        if (!Uri.TryCreate(baseFolderUrl, UriKind.Absolute, out var baseUri)) return null;

        var folderUri = new Uri(baseUri, EnsureTrailingSlash(versionFolderName.Trim()) + "/");
        folderUri = NormalizePassepartoutUrl(folderUri);

        var selectedFileName = selectedFileTemplate.Replace("{VERSION}", versionFolderName.Trim(), StringComparison.OrdinalIgnoreCase);

        var html = await GetStringWithRedirectsAsync(folderUri, ct, debug);
        if (html is null)
        {
            debug.Note($"Failed to fetch listing HTML for folderUri='{folderUri}'");
            debug.FlushToLast();
            return null;
        }

        var links = ExtractLinkTexts(html, folderUri);
        var match = links.FirstOrDefault(l =>
            !l.IsFolder &&
            string.Equals(l.Name, selectedFileName, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            debug.Note($"selectedFileName not found: '{selectedFileName}'");
            debug.SaveText("listing-links.txt", SummarizeLinks(links));
            debug.FlushToLast();
            return null;
        }

        debug.Note($"resolvedUrl='{match.Url}'");
        return match.Url.ToString();

        static string EnsureTrailingSlash(string s) => s.TrimEnd('/');
    }

    private async Task<Uri?> ResolveLatestFolderUrlAsync(string baseFolderUrl, bool pickLatestFolderByName, CancellationToken ct)
    {
        if (!Uri.TryCreate(baseFolderUrl, UriKind.Absolute, out var baseUri))
            return null;

        using var debug = new DownloadDebug(_log);
        var requestUri = NormalizePassepartoutUrl(baseUri);
        var html = await GetStringWithRedirectsAsync(requestUri, ct, debug);
        if (html is null) return null;

        var allLinks = ExtractLinkTexts(html, requestUri);
        debug.SaveText("listing-links.txt", SummarizeLinks(allLinks));

        var directoryPath = GetDirectoryPath(requestUri);

        var folderLinks = allLinks
            .Where(l => l.IsFolder)
            .Where(l => !string.IsNullOrWhiteSpace(l.Name))
            .Where(l => !string.Equals(GetDirectoryPath(l.Url), directoryPath, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.Url.AbsolutePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            .Where(l => !string.Equals(l.Name, "home", StringComparison.OrdinalIgnoreCase))
            .Where(l => !string.Equals(l.Name, "logout", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (folderLinks.Count == 0)
        {
            var hasExeFiles = allLinks.Any(l => !l.IsFolder && l.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            debug.Note($"No version subfolders found. hasExeFiles={hasExeFiles}");
            if (!hasExeFiles) debug.FlushToLast();
            return hasExeFiles ? requestUri : null;
        }

        LinkInfo latest;
        if (pickLatestFolderByName)
        {
            latest = folderLinks
                .OrderBy(l => l.Name, Comparer<string>.Create(CompareVersionFolderNames))
                .Last();
        }
        else
        {
            latest = folderLinks.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Last();
        }

        debug.Note($"latestFolderUrl='{latest.Url}', latestFolderName='{latest.Name}'");
        return latest.Url;
    }

    private async Task<string?> GetStringWithRedirectsAsync(Uri url, CancellationToken ct, DownloadDebug debug)
    {
        url = NormalizePassepartoutUrl(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithRedirectsAsync(request, ct);

        if (response is null) return null;
        if (!response.IsSuccessStatusCode)
        {
            _log.Warning($"Failed to fetch directory listing: HTTP {(int)response.StatusCode}");
            var html = await SafeReadAsStringAsync(response, ct);
            if (!string.IsNullOrWhiteSpace(html))
                debug.SaveHtml("listing-html.html", html);
            debug.Note($"HTTP {(int)response.StatusCode} for url='{url}'");
            return null;
        }

        var okHtml = await response.Content.ReadAsStringAsync(ct);
        debug.SaveHtml("listing-html.html", okHtml);
        debug.Note($"Fetched url='{url}', length={okHtml.Length}");
        return okHtml;
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
                : new Uri(currentRequest.RequestUri ?? urlFallback(), location);

            currentRequest.Dispose();
            currentRequest = new HttpRequestMessage(HttpMethod.Get, nextUri);
        }

        return response;

        static Uri urlFallback() => new("https://download.passepartout.cloud/.");
    }

    private static bool IsRedirectStatusCode(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static List<LinkInfo> ExtractLinkTexts(string html, Uri baseUri)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var links = doc.DocumentNode.SelectNodes("//a[@href]");
        if (links is null) return new List<LinkInfo>();

        var result = new List<LinkInfo>(links.Count);

        foreach (var node in links)
        {
            var href = node.GetAttributeValue("href", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.StartsWith("#", StringComparison.OrdinalIgnoreCase)) continue;
            if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;

            var hrefWithoutQuery = href.Split('?', '#').FirstOrDefault() ?? href;
            var nameFromHref = hrefWithoutQuery.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;

            var name = nameFromHref;
            if (string.IsNullOrWhiteSpace(name))
                name = WebUtility.HtmlDecode(node.InnerText).Trim();

            if (string.IsNullOrWhiteSpace(name)) continue;

            var url = Uri.TryCreate(baseUri, href, out var absolute) ? absolute : null;
            if (url is null) continue;

            var isFolder = hrefWithoutQuery.EndsWith("/", StringComparison.OrdinalIgnoreCase) ||
                           (url.AbsolutePath.EndsWith("/", StringComparison.OrdinalIgnoreCase) && !url.AbsolutePath.EndsWith("/.", StringComparison.OrdinalIgnoreCase));

            result.Add(new LinkInfo(name.TrimEnd('/'), url, isFolder));
        }

        return result;
    }

    private static int CompareVersionFolderNames(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var a = left.Trim().TrimEnd('/');
        var b = right.Trim().TrimEnd('/');

        if (VersionFolderRegex.Match(a) is { Success: true } ma &&
            VersionFolderRegex.Match(b) is { Success: true } mb &&
            int.TryParse(ma.Groups["year"].Value, out var yearA) &&
            int.TryParse(mb.Groups["year"].Value, out var yearB))
        {
            var cmpYear = yearA.CompareTo(yearB);
            if (cmpYear != 0) return cmpYear;

            var suffixA = ma.Groups["suffix"].Value;
            var suffixB = mb.Groups["suffix"].Value;
            return string.Compare(suffixA, suffixB, StringComparison.OrdinalIgnoreCase);
        }

        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LinkInfo(string Name, Uri Url, bool IsFolder);

    private static Uri NormalizePassepartoutUrl(Uri url)
    {
        if (!string.Equals(url.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.AbsolutePath.EndsWith("/.", StringComparison.OrdinalIgnoreCase))
            return url;

        var builder = new UriBuilder(url)
        {
            Path = builderPath(url)
        };
        return builder.Uri;

        static string builderPath(Uri u)
        {
            var path = u.AbsolutePath.TrimEnd('/');
            return string.IsNullOrWhiteSpace(path) ? "/." : path + "/.";
        }
    }

    private static string SummarizeLinks(List<LinkInfo> links)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"count={links.Count}");

        foreach (var l in links.Take(200))
            sb.AppendLine($"{(l.IsFolder ? "DIR " : "FILE")} name='{l.Name}' url='{l.Url}'");

        return sb.ToString();
    }

    private static async Task<string?> SafeReadAsStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return null; }
    }

    private sealed class DownloadDebug : IDisposable
    {
        private readonly ILogService _log;
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _notes = new();

        public DownloadDebug(ILogService log)
        {
            _log = log;
        }

        public void Note(string message)
        {
            _notes.Add(message);
            _files["notes.txt"] = string.Join(Environment.NewLine, _notes);
        }

        public void SaveHtml(string name, string html) => SaveText(name, html);

        public void SaveText(string name, string text)
        {
            _files[name] = TrimToLimit(text);
        }

        public void FlushToLast()
        {
            var lastDir = Path.Combine(GetStorageDir(), "download_debug", "last");
            try
            {
                if (Directory.Exists(lastDir))
                    Directory.Delete(lastDir, recursive: true);
            }
            catch { }

            Directory.CreateDirectory(lastDir);
            foreach (var kvp in _files)
                File.WriteAllText(Path.Combine(lastDir, kvp.Key), kvp.Value);

            _log.Info($"Download debug written to: {lastDir}");
        }

        public void Dispose()
        {
        }

        private static string GetStorageDir()
        {
            var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
            return string.IsNullOrWhiteSpace(overrideDir)
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : overrideDir;
        }

        private static string TrimToLimit(string text)
        {
            const int limit = 200_000;
            if (text.Length <= limit) return text;
            return text[..limit];
        }
    }

    private static string GetDirectoryPath(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.EndsWith("/.", StringComparison.OrdinalIgnoreCase))
            path = path[..^2];
        if (!path.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            path += "/";
        return path;
    }
}

