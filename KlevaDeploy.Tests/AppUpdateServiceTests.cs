using System.Net;
using System.Net.Http;
using System.Text;
using KlevaDeploy.Models;
using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Tests;

[Collection("EnvVar")]
public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdateAsync_ReturnsRichReleaseMetadataForNewerGitHubRelease()
    {
        const string json = """
        {
          "tag_name": "v0.3.0",
          "name": "v0.3.0",
          "body": "Bug fixes\n- Added in-app updates",
          "html_url": "https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.3.0",
          "prerelease": false,
          "published_at": "2026-07-01T18:00:00Z",
          "assets": [
            {
              "name": "KlevaDeploy.exe",
              "browser_download_url": "https://github.com/Spadehub/KlevaDeploy/releases/download/v0.3.0/KlevaDeploy.exe",
              "size": 123456
            }
          ]
        }
        """;

        using var _ = new EnvVarScope(("KLEVADEPLOY_APP_VERSION_OVERRIDE", "0.2.3"));
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(httpClient, new FakeLogService(), new FakeAppConfigService());

        var info = await service.CheckForUpdateAsync();

        Assert.NotNull(info);
        Assert.Equal("0.3.0", info!.Version);
        Assert.Equal("KlevaDeploy.exe", info.AssetName);
        Assert.Equal("v0.3.0", info.ReleaseName);
        Assert.Contains("Added in-app updates", info.ReleaseNotes);
        Assert.Equal("https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.3.0", info.ReleasePageUrl);
        Assert.False(info.IsPrerelease);
        Assert.Equal(123456, info.AssetSizeBytes);
        Assert.Equal(DateTimeOffset.Parse("2026-07-01T18:00:00Z"), info.PublishedAtUtc);
    }

    [Fact]
    public async Task DownloadUpdateAsync_ReusesExistingDownloadedAssetWhenSizeMatches()
    {
        var storageDir = Path.Combine(Path.GetTempPath(), "KlevaDeployTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageDir);

        try
        {
            using var _ = new EnvVarScope(("KLEVADEPLOY_STORAGE_DIR", storageDir));
            var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("Network download should not be used."));
            using var httpClient = new HttpClient(handler);
            var service = new AppUpdateService(httpClient, new FakeLogService(), new FakeAppConfigService());

            var existingDir = Path.Combine(storageDir, "app_updates");
            Directory.CreateDirectory(existingDir);
            var expectedPath = Path.Combine(existingDir, "KlevaDeploy-0.3.0.exe");
            await File.WriteAllBytesAsync(expectedPath, [1, 2, 3, 4]);

            var info = new AppUpdateInfo(
                "0.3.0",
                "https://example.invalid/KlevaDeploy.exe",
                "KlevaDeploy.exe",
                "v0.3.0",
                string.Empty,
                string.Empty,
                false,
                null,
                4);

            var downloadedPath = await service.DownloadUpdateAsync(info);

            Assert.Equal(expectedPath, downloadedPath);
            Assert.Equal(0, handler.CallCount);
        }
        finally
        {
            if (Directory.Exists(storageDir))
                Directory.Delete(storageDir, true);
        }
    }

    [Fact]
    public async Task CheckForUpdateAsync_DetectsNewerFourPartPatchRelease()
    {
        const string json = """
        {
          "tag_name": "v0.2.2.2",
          "name": "v0.2.2.2",
          "body": "Patch release",
          "html_url": "https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.2.2.2",
          "prerelease": false,
          "published_at": "2026-07-02T20:00:00Z",
          "assets": [
            {
              "name": "KlevaDeploy.exe",
              "browser_download_url": "https://github.com/Spadehub/KlevaDeploy/releases/download/v0.2.2.2/KlevaDeploy.exe",
              "size": 654321
            }
          ]
        }
        """;

        using var _ = new EnvVarScope(("KLEVADEPLOY_APP_VERSION_OVERRIDE", "0.2.2.1"));
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(httpClient, new FakeLogService(), new FakeAppConfigService());

        var info = await service.CheckForUpdateAsync();

        Assert.NotNull(info);
        Assert.Equal("0.2.2.2", info!.Version);
        Assert.Equal("https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.2.2.2", info.ReleasePageUrl);
    }

    [Fact]
    public async Task CheckForUpdateAsync_DoesNotOfferSameFourPartVersion()
    {
        const string json = """
        {
          "tag_name": "v0.2.2.2",
          "name": "v0.2.2.2",
          "body": "Patch release",
          "html_url": "https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.2.2.2",
          "prerelease": false,
          "published_at": "2026-07-02T20:00:00Z",
          "assets": [
            {
              "name": "KlevaDeploy.exe",
              "browser_download_url": "https://github.com/Spadehub/KlevaDeploy/releases/download/v0.2.2.2/KlevaDeploy.exe",
              "size": 654321
            }
          ]
        }
        """;

        using var _ = new EnvVarScope(("KLEVADEPLOY_APP_VERSION_OVERRIDE", "0.2.2.2"));
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler);
        var service = new AppUpdateService(httpClient, new FakeLogService(), new FakeAppConfigService());

        var info = await service.CheckForUpdateAsync();

        Assert.Null(info);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }

    private sealed class FakeLogService : ILogService
    {
        public IReadOnlyList<LogEntry> Entries => Array.Empty<LogEntry>();
        public event EventHandler<LogEntry>? LogAdded { add { } remove { } }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void AppendRaw(string level, string message) { }
    }

    private sealed class FakeAppConfigService : IAppConfigService
    {
        public AppConfig Config { get; } = new()
        {
            AppUpdateService = new AppUpdateServiceConfig
            {
                Owner = "Spadehub",
                Repo = "KlevaDeploy",
                LegacyRepo = string.Empty,
                AssetName = "KlevaDeploy.exe",
                TokenEnvVar = "KLEVADEPLOY_GITHUB_TOKEN"
            }
        };
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvVarScope()
        {
        }

        public EnvVarScope(params (string Name, string? Value)[] changes)
        {
            foreach (var (name, value) in changes)
            {
                _previousValues[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _previousValues)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
