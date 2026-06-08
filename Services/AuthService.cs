using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using HtmlAgilityPack;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private readonly ILogService _log;
    private readonly AuthServiceConfig _cfg;
    private string _lastPortalHomeUrl = string.Empty;
    private readonly Dictionary<string, bool> _authByHost = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAuthenticated { get; private set; }
    public int AuthenticatedPortalCount => _authByHost.Count(kvp => kvp.Value);

    public event EventHandler? AuthStateChanged;

    public AuthService(HttpClient httpClient, CookieContainer cookieContainer, ILogService log, IAppConfigService config)
    {
        _httpClient = httpClient;
        _cookieContainer = cookieContainer;
        _log = log;
        _cfg = config.Config.AuthService;
        _lastPortalHomeUrl = _cfg.DownloadsHomeUrl ?? string.Empty;
    }

    public bool IsAuthenticatedForUrl(string url)
    {
        if (!Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out var uri))
            return false;

        lock (_authByHost)
        {
            if (_authByHost.TryGetValue(uri.Host, out var authed))
                return authed;
        }

        try
        {
            var cookies = _cookieContainer.GetCookies(uri);
            return cookies is not null && cookies.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool IsAuthenticatedForPortalHomeUrl(string portalHomeUrl)
    {
        var normalized = NormalizePortalHomeUrl(portalHomeUrl);
        return IsAuthenticatedForUrl(normalized);
    }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        return await LoginAsync(username, password, _cfg.DownloadsHomeUrl, ct);
    }

    public async Task<bool> LoginAsync(string username, string password, string portalHomeUrl, CancellationToken ct = default)
    {
        try
        {
            EnsureDefaultHeaders();

            var normalizedPortalHomeUrl = NormalizePortalHomeUrl(portalHomeUrl);
            _lastPortalHomeUrl = normalizedPortalHomeUrl;
            _log.Info($"Attempting portal login: {normalizedPortalHomeUrl}");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(25));

            var netOk = await TryLoginPassepartoutNetAsync(username, password, timeoutCts.Token);
            var downloadsOk = await TryLoginPassepartoutDownloadsAsync(username, password, normalizedPortalHomeUrl, timeoutCts.Token);

            var portalHost = TryGetHost(normalizedPortalHomeUrl);
            if (!string.IsNullOrWhiteSpace(portalHost))
            {
                lock (_authByHost)
                {
                    if (downloadsOk)
                        _authByHost[portalHost] = true;
                }
            }

            IsAuthenticated = (downloadsOk || netOk) || AuthenticatedPortalCount > 0;

            if (IsAuthenticated)
            {
                _log.Info("Portal login successful.");
                SaveSessionIfEnabled();
                AuthStateChanged?.Invoke(this, EventArgs.Empty);
            }
            else
                _log.Warning("Portal login failed.");

            return IsAuthenticated;
        }
        catch (OperationCanceledException)
        {
            _log.Warning("Portal login timed out.");
            IsAuthenticated = false;
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Login error", ex);
            IsAuthenticated = false;
            return false;
        }
    }

    public void Logout()
    {
        lock (_authByHost)
        {
            _authByHost.Clear();
        }

        IsAuthenticated = false;
        TryClearInMemoryCookies();
        TryClearPersistedSession();
        _log.Info("Session logged out.");
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void LogoutPortal(string portalHomeUrl)
    {
        var normalized = NormalizePortalHomeUrl(portalHomeUrl);
        var host = TryGetHost(normalized);
        if (string.IsNullOrWhiteSpace(host)) return;

        TryClearCookiesForHost(host);

        lock (_authByHost)
        {
            _authByHost[host] = false;
        }

        IsAuthenticated = AuthenticatedPortalCount > 0;
        AuthStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        if (!IsSessionPersistenceEnabled())
            return false;

        try
        {
            if (!TryLoadPersistedSession())
                return false;

            EnsureDefaultHeaders();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            string? html = null;
            try
            {
                using var verify = await GetWithRedirectsAsync(new Uri(_lastPortalHomeUrl), timeoutCts.Token, debug: null);
                if (verify is not null && verify.IsSuccessStatusCode)
                    html = await verify.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch { }

            if (string.IsNullOrWhiteSpace(html))
            {
                using var verify = await GetWithRedirectsAsync(new Uri((_cfg.DownloadsHomeUrl ?? string.Empty).Trim()), timeoutCts.Token, debug: null);
                if (verify is not null && verify.IsSuccessStatusCode)
                    html = await verify.Content.ReadAsStringAsync(timeoutCts.Token);
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                IsAuthenticated = false;
                return false;
            }
            var ok = html.Contains("Folder Path", StringComparison.OrdinalIgnoreCase) ||
                     html.Contains("Log out", StringComparison.OrdinalIgnoreCase);

            IsAuthenticated = ok;
            if (ok)
            {
                var host = TryGetHost(_lastPortalHomeUrl);
                if (!string.IsNullOrWhiteSpace(host))
                {
                    lock (_authByHost)
                    {
                        _authByHost[host] = true;
                    }
                }
            }
            return ok;
        }
        catch
        {
            IsAuthenticated = false;
            return false;
        }
    }

    private void EnsureDefaultHeaders()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("Accept-Language"))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "it-IT,it;q=0.9,en-US;q=0.8,en;q=0.7");
        }
    }

    private static bool IsSessionPersistenceEnabled()
    {
        var v = Environment.GetEnvironmentVariable("KLEVADEPLOY_PERSIST_AUTH");
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveSessionIfEnabled()
    {
        if (!IsSessionPersistenceEnabled())
            return;

        try
        {
            var storageDir = GetStorageDir();
            Directory.CreateDirectory(storageDir);
            var path = Path.Combine(storageDir, "auth_session.json");

            var list = new List<PersistedCookie>();
            foreach (var uri in GetKnownAuthUris(_lastPortalHomeUrl))
            {
                foreach (Cookie c in _cookieContainer.GetCookies(uri))
                {
                    if (c.Expired) continue;
                    list.Add(PersistedCookie.FromCookie(c));
                }
            }

            var session = new PersistedAuthSession
            {
                PortalHomeUrl = _lastPortalHomeUrl,
                Cookies = list
            };
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            _log.Info($"Auth session persisted: {path}");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to persist auth session", ex);
        }
    }

    private bool TryLoadPersistedSession()
    {
        try
        {
            var storageDir = GetStorageDir();
            var path = Path.Combine(storageDir, "auth_session.json");
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var session = JsonSerializer.Deserialize<PersistedAuthSession>(json);
            List<PersistedCookie> list;

            if (session is not null && session.Cookies.Count > 0)
            {
                _lastPortalHomeUrl = NormalizePortalHomeUrl(session.PortalHomeUrl);
                list = session.Cookies;
            }
            else
            {
                list = JsonSerializer.Deserialize<List<PersistedCookie>>(json) ?? new List<PersistedCookie>();
            }

            foreach (var pc in list)
            {
                var cookie = pc.ToCookie();
                if (cookie.Expired) continue;
                try { _cookieContainer.Add(cookie); }
                catch { }
            }

            _log.Info("Auth session restored from disk.");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to restore auth session", ex);
            return false;
        }
    }

    private void TryClearPersistedSession()
    {
        try
        {
            var storageDir = GetStorageDir();
            var path = Path.Combine(storageDir, "auth_session.json");
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private void TryClearInMemoryCookies()
    {
        try
        {
            var domainTable = typeof(CookieContainer).GetField("m_domainTable", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_cookieContainer);
            if (domainTable is System.Collections.IDictionary dict)
                dict.Clear();
        }
        catch { }
    }

    private void TryClearCookiesForHost(string host)
    {
        try
        {
            var domainTable = typeof(CookieContainer).GetField("m_domainTable", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_cookieContainer);
            if (domainTable is not System.Collections.IDictionary dict) return;

            var keysToRemove = new List<object>();
            foreach (var key in dict.Keys)
            {
                if (key is null) continue;
                var s = key?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(s)) continue;

                var normalized = s.TrimStart('.');
                if (string.Equals(normalized, host, StringComparison.OrdinalIgnoreCase))
                    keysToRemove.Add(key!);
            }

            foreach (var k in keysToRemove)
                dict.Remove(k);
        }
        catch { }
    }

    private static string? TryGetHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Host;
    }

    private static IEnumerable<Uri> GetKnownAuthUris(string portalHomeUrl)
    {
        if (Uri.TryCreate(portalHomeUrl, UriKind.Absolute, out var portal))
        {
            yield return portal;
            yield return new Uri(portal.GetLeftPart(UriPartial.Authority) + "/");
        }

        yield return new Uri("https://download.passepartout.cloud/");
        yield return new Uri("https://idp.passepartout.cloud/");
        yield return new Uri("https://www.passepartout.net/");
    }

    private static string GetStorageDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    private sealed record PersistedCookie(
        string Name,
        string Value,
        string Domain,
        string Path,
        DateTimeOffset? ExpiresUtc,
        bool Secure,
        bool HttpOnly)
    {
        public static PersistedCookie FromCookie(Cookie c) =>
            new(
                c.Name,
                c.Value,
                c.Domain,
                c.Path,
                c.Expires == DateTime.MinValue ? null : new DateTimeOffset(DateTime.SpecifyKind(c.Expires, DateTimeKind.Utc)),
                c.Secure,
                c.HttpOnly);

        public Cookie ToCookie()
        {
            var c = new Cookie(Name, Value, Path, Domain)
            {
                Secure = Secure,
                HttpOnly = HttpOnly
            };

            if (ExpiresUtc is not null)
                c.Expires = ExpiresUtc.Value.UtcDateTime;

            return c;
        }
    }

    private sealed class PersistedAuthSession
    {
        public string PortalHomeUrl { get; set; } = string.Empty;
        public List<PersistedCookie> Cookies { get; set; } = new();
    }

    private async Task<bool> TryLoginPassepartoutNetAsync(string username, string password, CancellationToken ct)
    {
        try
        {
            var loginPageUrl = (_cfg.LoginPageUrl ?? string.Empty).Trim();
            var loginPostUrl = (_cfg.LoginPostUrl ?? string.Empty).Trim();
            _httpClient.DefaultRequestHeaders.Referrer = new Uri(loginPageUrl);
            var getResponse = await _httpClient.GetAsync(loginPageUrl, ct);
            if (!getResponse.IsSuccessStatusCode) return false;

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
            });

            var postResponse = await _httpClient.PostAsync(loginPostUrl, formData, ct);
            return postResponse.IsSuccessStatusCode || (int)postResponse.StatusCode == 302;
        }
        catch (Exception ex)
        {
            _log.Error("Passepartout.net login error", ex);
            return false;
        }
    }

    private async Task<bool> TryLoginPassepartoutDownloadsAsync(string username, string password, string portalHomeUrl, CancellationToken ct)
    {
        using var debug = new AuthDebug(_log);
        try
        {
            var portalUri = new Uri(NormalizePortalHomeUrl(portalHomeUrl));
            using var landing = await GetWithRedirectsAsync(portalUri, ct, debug);
            if (landing is null)
            {
                debug.FlushToLast();
                return false;
            }

            var landingHtml = await landing.Content.ReadAsStringAsync(ct);
            debug.SaveHtml("downloads-landing.html", landingHtml);

            if (landing.StatusCode == HttpStatusCode.Unauthorized)
            {
                debug.FlushToLast();
                return false;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(landingHtml);

            var formNode = doc.DocumentNode.SelectSingleNode("//form[.//input[translate(@type,'PASSWORD','password')='password']]");
            formNode ??= doc.DocumentNode.SelectSingleNode("//form");
            if (formNode is null)
            {
                debug.FlushToLast();
                return false;
            }

            var actionAttr = formNode.GetAttributeValue("action", string.Empty);
            if (string.IsNullOrWhiteSpace(actionAttr))
            {
                debug.FlushToLast();
                return false;
            }

            var baseUri = landing.RequestMessage?.RequestUri ?? portalUri;
            var actionUri = new Uri(baseUri, actionAttr);

            var inputNodes = formNode.SelectNodes(".//input[@name]") ?? new HtmlNodeCollection(formNode);

            string? passwordField = null;
            var usernameCandidates = new List<(string Name, int Score)>();
            var otherPairs = new List<KeyValuePair<string, string>>();
            var submitPairs = new List<KeyValuePair<string, string>>();

            foreach (var input in inputNodes)
            {
                var name = input.GetAttributeValue("name", string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var type = input.GetAttributeValue("type", string.Empty).Trim().ToLowerInvariant();
                var id = input.GetAttributeValue("id", string.Empty).Trim();

                if (type == "password")
                {
                    passwordField ??= name;
                    continue;
                }

                if (type is "text" or "email")
                {
                    var score = 0;
                    if (string.Equals(name, "username", StringComparison.OrdinalIgnoreCase)) score += 100;
                    if (string.Equals(id, "username", StringComparison.OrdinalIgnoreCase)) score += 100;
                    if (name.Contains("user", StringComparison.OrdinalIgnoreCase) || id.Contains("user", StringComparison.OrdinalIgnoreCase)) score += 10;
                    if (name.Contains("email", StringComparison.OrdinalIgnoreCase) || id.Contains("email", StringComparison.OrdinalIgnoreCase)) score += 5;
                    usernameCandidates.Add((name, score));
                    continue;
                }

                var value = input.GetAttributeValue("value", string.Empty);
                if (type == "hidden")
                {
                    otherPairs.Add(new KeyValuePair<string, string>(name, value));
                    continue;
                }

                if (type == "submit")
                {
                    submitPairs.Add(new KeyValuePair<string, string>(name, value));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(value))
                    otherPairs.Add(new KeyValuePair<string, string>(name, value));
            }

            var buttonSubmit = formNode.SelectSingleNode(".//button[@type='submit' and @name]");
            if (buttonSubmit is not null)
            {
                var btnName = buttonSubmit.GetAttributeValue("name", string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(btnName))
                {
                    var btnValue = buttonSubmit.GetAttributeValue("value", string.Empty);
                    submitPairs.Add(new KeyValuePair<string, string>(btnName, btnValue));
                }
            }

            var usernameField = usernameCandidates
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => c.Name)
                .FirstOrDefault();

            usernameField ??= "username";
            passwordField ??= "password";

            var pairs = new List<KeyValuePair<string, string>>();
            pairs.AddRange(otherPairs);
            pairs.Add(new KeyValuePair<string, string>(usernameField, username));
            pairs.Add(new KeyValuePair<string, string>(passwordField, password));
            pairs.AddRange(submitPairs);

            debug.SaveText("downloads-form-fields.txt", BuildFieldsSummary(usernameField, passwordField, otherPairs, submitPairs));

            using var content = new FormUrlEncodedContent(pairs);
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, actionUri) { Content = content };
            postRequest.Headers.Referrer = baseUri;

            using var postResponse = await SendWithRedirectsAsync(postRequest, ct, debug);
            if (postResponse is null)
            {
                debug.FlushToLast();
                return false;
            }

            var postHtml = await postResponse.Content.ReadAsStringAsync(ct);
            debug.SaveHtml("downloads-after-post.html", postHtml);

            using var verify = await GetWithRedirectsAsync(portalUri, ct, debug);
            if (verify is null || !verify.IsSuccessStatusCode)
            {
                debug.FlushToLast();
                return false;
            }
            var verifyHtml = await verify.Content.ReadAsStringAsync(ct);
            debug.SaveHtml("downloads-verify.html", verifyHtml);

            var ok = verifyHtml.Contains("Folder Path", StringComparison.OrdinalIgnoreCase) ||
                     verifyHtml.Contains("Log out", StringComparison.OrdinalIgnoreCase);

            if (!ok)
                debug.FlushToLast();

            return ok;
        }
        catch (OperationCanceledException)
        {
            debug.FlushToLast();
            return false;
        }
        catch (Exception ex)
        {
            debug.FlushToLast();
            _log.Error("Passepartout downloads login error", ex);
            return false;
        }
    }

    private static string BuildFieldsSummary(
        string usernameField,
        string passwordField,
        List<KeyValuePair<string, string>> hiddenPairs,
        List<KeyValuePair<string, string>> submitPairs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"usernameField={usernameField}");
        sb.AppendLine($"passwordField={passwordField}");
        sb.AppendLine($"hiddenCount={hiddenPairs.Count}");
        foreach (var p in hiddenPairs)
            sb.AppendLine($"hidden:{p.Key}={(string.IsNullOrEmpty(p.Value) ? "<empty>" : "<present>")}");
        sb.AppendLine($"submitCount={submitPairs.Count}");
        foreach (var p in submitPairs)
            sb.AppendLine($"submit:{p.Key}={(string.IsNullOrEmpty(p.Value) ? "<empty>" : "<present>")}");
        return sb.ToString();
    }

    private async Task<HttpResponseMessage?> GetWithRedirectsAsync(Uri url, CancellationToken ct, AuthDebug? debug = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendWithRedirectsAsync(request, ct, debug);
    }

    private async Task<HttpResponseMessage?> SendWithRedirectsAsync(HttpRequestMessage request, CancellationToken ct, AuthDebug? debug = null)
    {
        const int maxRedirects = 10;
        HttpResponseMessage? response = null;
        var currentRequest = request;

        for (int i = 0; i < maxRedirects; i++)
        {
            response?.Dispose();
            response = await _httpClient.SendAsync(currentRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            debug?.Trace(currentRequest, response);

            if (!IsRedirectStatusCode(response.StatusCode))
                return response;

            var location = response.Headers.Location;
            if (location is null) return response;

            var nextUri = location.IsAbsoluteUri
                ? location
                : new Uri(currentRequest.RequestUri ?? new Uri(_lastPortalHomeUrl), location);

            currentRequest.Dispose();
            currentRequest = new HttpRequestMessage(HttpMethod.Get, nextUri);
        }

        return response;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode code) =>
        code is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private string NormalizePortalHomeUrl(string? portalHomeUrl)
    {
        var raw = (portalHomeUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return _cfg.DownloadsHomeUrl;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            if (!raw.Contains("://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate("https://" + raw, UriKind.Absolute, out var httpsUri))
            {
                uri = httpsUri;
            }
            else
            {
                return _cfg.DownloadsHomeUrl;
            }
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return _cfg.DownloadsHomeUrl;
        }

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };

        if (string.Equals(builder.Host, "download.passepartout.cloud", StringComparison.OrdinalIgnoreCase))
        {
            var path = (builder.Path ?? string.Empty).TrimEnd('/');
            builder.Path = string.IsNullOrWhiteSpace(path) ? "/." : path + "/.";
        }

        return builder.Uri.ToString();
    }

    private sealed class AuthDebug : IDisposable
    {
        private readonly bool _enabled;
        private readonly ILogService _log;
        private readonly List<string> _trace = new();
        private readonly string? _dir;
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public AuthDebug(ILogService log)
        {
            _log = log;
            var v = Environment.GetEnvironmentVariable("KLEVADEPLOY_AUTH_DEBUG");
            _enabled = string.Equals(v, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

            if (_enabled)
            {
                _dir = Path.Combine(GetStorageDir(), "auth_debug", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(_dir);
                _log.Info($"Auth debug enabled. Output: {_dir}");
            }
        }

        public void Trace(HttpRequestMessage request, HttpResponseMessage response)
        {
            var method = request.Method.Method;
            var url = request.RequestUri?.ToString() ?? string.Empty;
            var status = (int)response.StatusCode;
            var location = response.Headers.Location?.ToString();

            var wwwAuth = response.Headers.WwwAuthenticate is null
                ? string.Empty
                : string.Join(" | ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

            var setCookieCount = response.Headers.TryGetValues("Set-Cookie", out var setCookies) ? setCookies.Count() : 0;

            var line =
                $"{method} {url} -> {status}" +
                (string.IsNullOrWhiteSpace(location) ? "" : $" Location={location}") +
                (string.IsNullOrWhiteSpace(wwwAuth) ? "" : $" WWW-Authenticate={wwwAuth}") +
                (setCookieCount == 0 ? "" : $" Set-Cookie={setCookieCount}");

            _trace.Add(line);
            SaveText("downloads-trace.txt", string.Join(Environment.NewLine, _trace));
        }

        public void SaveHtml(string name, string html)
        {
            SaveText(name, html);
        }

        public void SaveText(string name, string text)
        {
            _files[name] = TrimToLimit(text);
        }

        public void FlushToLast()
        {
            var lastDir = Path.Combine(GetStorageDir(), "auth_debug", "last");
            try
            {
                if (Directory.Exists(lastDir))
                    Directory.Delete(lastDir, recursive: true);
            }
            catch { }

            Directory.CreateDirectory(lastDir);
            FlushToDirectory(lastDir);
            _log.Info($"Auth debug written to: {lastDir}");
        }

        public void Dispose()
        {
            if (!_enabled || _dir is null) return;
            FlushToDirectory(_dir);
        }

        private void FlushToDirectory(string dir)
        {
            foreach (var kvp in _files)
            {
                var path = Path.Combine(dir, kvp.Key);
                File.WriteAllText(path, kvp.Value);
            }
        }

        private static string TrimToLimit(string text)
        {
            const int limit = 200_000;
            if (text.Length <= limit) return text;
            return text[..limit];
        }

        private static string GetStorageDir()
        {
            var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
            return string.IsNullOrWhiteSpace(overrideDir)
                ? Path.Combine(AppContext.BaseDirectory, "Data")
                : overrideDir;
        }
    }
}
