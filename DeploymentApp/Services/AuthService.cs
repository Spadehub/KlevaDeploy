using System.Net;
using System.Net.Http;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.Services;

public sealed class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _log;

    // Passepartout portal endpoints — update these to the real URLs
    private const string LoginPageUrl = "https://www.passepartout.net/area-riservata/login";
    private const string LoginPostUrl = "https://www.passepartout.net/area-riservata/login";

    public bool IsAuthenticated { get; private set; }

    public AuthService(HttpClient httpClient, ILogService log)
    {
        _httpClient = httpClient;
        _log = log;
    }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        try
        {
            _log.Info("Attempting Passepartout login...");

            // First GET to obtain any CSRF token / session cookie
            var getResponse = await _httpClient.GetAsync(LoginPageUrl, ct);
            getResponse.EnsureSuccessStatusCode();

            // POST credentials simulating a browser form submission
            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
            });

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Referrer = new Uri(LoginPageUrl);

            var postResponse = await _httpClient.PostAsync(LoginPostUrl, formData, ct);

            // Treat redirect-after-POST (302/200 on dashboard) as success
            IsAuthenticated = postResponse.IsSuccessStatusCode || (int)postResponse.StatusCode == 302;

            if (IsAuthenticated)
                _log.Info("Passepartout login successful.");
            else
                _log.Warning($"Passepartout login failed. HTTP {postResponse.StatusCode}");

            return IsAuthenticated;
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
        IsAuthenticated = false;
        _log.Info("Session logged out.");
    }
}
