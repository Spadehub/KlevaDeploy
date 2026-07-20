using System.Net;
using System.Net.Http;
using System.Text;
using KlevaDeploy.Models;
using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Tests;

[Collection("EnvVar")]
public sealed class UpdateServiceTests
{
    [Fact]
    public async Task UpdateSingleInstallerAsync_WhenCacheMissing_UsesCacheWording()
    {
        var storageDir = Path.Combine(Path.GetTempPath(), "KlevaDeployTests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(storageDir, "installers", "retail", "RetailSetup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

        try
        {
            using var _ = new EnvVarScope(("KLEVADEPLOY_STORAGE_DIR", storageDir));
            var log = new LogService();
            using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("https://example.invalid/retail.exe", request.RequestUri!.ToString());
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("retail-installer"))
                };
            }));

            var service = new UpdateService(httpClient, new FakeAuthService(), new FakeDirectoryListingService(), log);
            var process = new DeploymentProcess
            {
                Id = "retail-server",
                Name = "Passepartout Retail Server",
                Kind = ProcessKind.Installer,
                InstallerSourceMode = InstallerSourceMode.StaticWeb,
                DownloadUrl = "https://example.invalid/retail.exe",
                RelativePath = installerPath
            };

            await service.UpdateSingleInstallerAsync(process);

            Assert.True(File.Exists(installerPath));
            Assert.Contains(log.Entries, entry => entry.Message == "Caching installer for 'Passepartout Retail Server'...");
            Assert.Contains(log.Entries, entry => entry.Message.StartsWith("Installer cached for 'Passepartout Retail Server' (", StringComparison.Ordinal));
            Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("Downloading update", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("installer updated", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(storageDir))
                    Directory.Delete(storageDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task UpdateSingleInstallerAsync_WhenStaticInstallerAlreadyCached_DoesNotEmitCacheDownloadLog()
    {
        var storageDir = Path.Combine(Path.GetTempPath(), "KlevaDeployTests", Guid.NewGuid().ToString("N"));
        var installerPath = Path.Combine(storageDir, "installers", "retail", "RetailSetup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

        try
        {
            using var _ = new EnvVarScope(("KLEVADEPLOY_STORAGE_DIR", storageDir));
            await File.WriteAllTextAsync(installerPath, "cached");
            new InstallerUpdateState
            {
                Entries = new Dictionary<string, InstallerUpdateStateEntry>
                {
                    ["retail-server"] = new()
                    {
                        LastDownloadedFromUrl = "https://example.invalid/retail.exe"
                    }
                }
            }.Save(storageDir);

            var log = new LogService();
            using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => throw new InvalidOperationException("Download should not be used.")));
            var service = new UpdateService(httpClient, new FakeAuthService(), new FakeDirectoryListingService(), log);
            var process = new DeploymentProcess
            {
                Id = "retail-server",
                Name = "Passepartout Retail Server",
                Kind = ProcessKind.Installer,
                InstallerSourceMode = InstallerSourceMode.StaticWeb,
                DownloadUrl = "https://example.invalid/retail.exe",
                RelativePath = installerPath
            };

            await service.UpdateSingleInstallerAsync(process);

            Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("Caching installer", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("Refreshing cached installer", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("Downloading update", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(storageDir))
                    Directory.Delete(storageDir, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private sealed class FakeAuthService : IAuthService
    {
        public bool IsAuthenticated => false;
        public int AuthenticatedPortalCount => 0;
        public event EventHandler? AuthStateChanged { add { } remove { } }
        public bool IsAuthenticatedForUrl(string url) => false;
        public bool IsAuthenticatedForPortalHomeUrl(string portalHomeUrl) => false;
        public Task<bool> LoginAsync(string username, string password, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> LoginAsync(string username, string password, string portalHomeUrl, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> TryRestoreSessionAsync(CancellationToken ct = default) => Task.FromResult(false);
        public void LogoutPortal(string portalHomeUrl) { }
        public void Logout() { }
    }

    private sealed class FakeDirectoryListingService : IDownloadDirectoryListingService
    {
        public Task<LatestFolderExeListing?> GetLatestFolderExeListingAsync(string baseFolderUrl, bool pickLatestFolderByName, CancellationToken ct = default) =>
            Task.FromResult<LatestFolderExeListing?>(null);

        public Task<IReadOnlyList<string>> ListSubfoldersAsync(string baseFolderUrl, bool pickLatestFolderByName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<LatestFolderExeListing?> GetFolderExeListingAsync(string folderUrl, CancellationToken ct = default) =>
            Task.FromResult<LatestFolderExeListing?>(null);

        public Task<string?> ResolveDownloadUrlAsync(string baseFolderUrl, bool pickLatestFolderByName, string selectedFileTemplate, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> ResolveDownloadUrlAsync(string baseFolderUrl, bool pickLatestFolderByName, string selectedFileTemplate, string? versionFolderName, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previousValues;

        public EnvVarScope(params (string Name, string? Value)[] changes)
        {
            _previousValues = changes.ToDictionary(
                static change => change.Name,
                static change => Environment.GetEnvironmentVariable(change.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value) in changes)
                Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var pair in _previousValues)
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }
}
