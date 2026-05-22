using System.IO;
using System.Net.Http;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly IAuthService _authService;
    private readonly ILogService _log;

    public UpdateService(HttpClient httpClient, IAuthService authService, ILogService log)
    {
        _httpClient = httpClient;
        _authService = authService;
        _log = log;
    }

    public async Task CheckAndUpdateAsync(IReadOnlyList<SoftwarePackage> packages, CancellationToken ct = default)
    {
        if (!await IsInternetAvailableAsync(ct))
        {
            _log.Info("No internet connection. Using local fallback installers.");
            return;
        }

        _log.Info("Internet available. Checking for installer updates...");

        foreach (var pkg in packages.Where(p => !string.IsNullOrEmpty(p.DownloadUrl)))
        {
            if (pkg.RequiresAuth && !_authService.IsAuthenticated)
            {
                _log.Warning($"Skipping update for '{pkg.Name}': auth required but not logged in.");
                continue;
            }

            try
            {
                var localPath = Path.Combine(AppContext.BaseDirectory, pkg.LocalInstallerRelativePath);
                var dir = Path.GetDirectoryName(localPath);
                if (dir is not null) Directory.CreateDirectory(dir);

                _log.Info($"Downloading update for '{pkg.Name}'...");
                var bytes = await _httpClient.GetByteArrayAsync(pkg.DownloadUrl, ct);
                await File.WriteAllBytesAsync(localPath, bytes, ct);
                _log.Info($"'{pkg.Name}' installer updated ({bytes.Length} bytes).");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to update '{pkg.Name}'", ex);
            }
        }
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
