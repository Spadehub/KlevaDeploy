using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Tests;

public sealed class PresetViewToggleTests
{
    [Fact]
    public void MainViewModel_LoadsPresetViewModeFromPreferences()
    {
        var prefs = new UserPreferences { PresetsViewMode = PresetsViewMode.Grid };
        var prefsService = new FakePreferencesService(prefs);

        var vm = CreateMainViewModel(prefsService);

        Assert.True(vm.IsPresetGridView);
    }

    [Fact]
    public void ToggleCommands_UpdatePreferencesAndPersist()
    {
        var prefs = new UserPreferences { PresetsViewMode = PresetsViewMode.List };
        var prefsService = new FakePreferencesService(prefs);

        var vm = CreateMainViewModel(prefsService);

        vm.IsPresetGridView = true;
        Assert.True(vm.IsPresetGridView);
        Assert.Equal(PresetsViewMode.Grid, prefs.PresetsViewMode);

        vm.IsPresetGridView = false;
        Assert.False(vm.IsPresetGridView);
        Assert.Equal(PresetsViewMode.List, prefs.PresetsViewMode);

        Assert.True(prefsService.SaveCount >= 2);
    }

    private static MainViewModel CreateMainViewModel(IPreferencesService prefsService)
    {
        var installer = new FakeInstallerService();
        var update = new FakeUpdateService();
        var auth = new FakeAuthService();
        var downloads = new FakeDownloadDirectoryListingService();
        var appUpdate = new FakeAppUpdateService();
        var processExecution = new FakeProcessExecutionService();
        var licenseScraper = new FakeLicenseScraperService();
        var log = new FakeLogService();
        var clipboard = new FakeClipboardService();
        var theme = new FakeThemeService();
        var dialog = new FakeDialogService();
        var presetIcon = new FakePresetIconService();
        var logVm = new LogViewModel(log, clipboard);

        return new MainViewModel(
            installer,
            update,
            auth,
            downloads,
            appUpdate,
            processExecution,
            licenseScraper,
            log,
            theme,
            dialog,
            presetIcon,
            prefsService,
            loginVmFactory: () => new LoginViewModel(auth, prefsService),
            logViewModel: logVm);
    }

    private sealed class FakePreferencesService(UserPreferences prefs) : IPreferencesService
    {
        public UserPreferences Preferences { get; } = prefs;
        public int SaveCount { get; private set; }
        public void Save() => SaveCount++;
    }

    private sealed class FakeInstallerService : IInstallerService
    {
        public Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync(bool isDemoMode) =>
            Task.FromResult<IReadOnlyList<DeploymentPreset>>(Array.Empty<DeploymentPreset>());

        public Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync(bool isDemoMode) =>
            Task.FromResult<IReadOnlyList<DeploymentProcess>>(Array.Empty<DeploymentProcess>());

        public IReadOnlyList<(DeploymentProcess Process, int Order, bool IsRequired)> BuildExecutionQueue(
            IEnumerable<DeploymentPreset> selectedPresets,
            IReadOnlyList<DeploymentProcess> allProcesses) => Array.Empty<(DeploymentProcess, int, bool)>();

        public string ResolveProcessPath(DeploymentProcess process) => string.Empty;
        public void AddUserPreset(DeploymentPreset preset) { }
        public IReadOnlyList<DeploymentProcess> GetAllAvailableProcesses() => Array.Empty<DeploymentProcess>();
        public IReadOnlyList<DeploymentPreset> GetAllPresets() => Array.Empty<DeploymentPreset>();
        public void AddUserProcess(DeploymentProcess process) { }
        public void UpdatePreset(DeploymentPreset preset) { }
        public void UpdateProcess(DeploymentProcess process) { }
        public bool DeletePreset(string presetId) => false;
        public bool DeleteProcess(string processId) => false;
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public Task CheckAndUpdateInstallersAsync(IReadOnlyList<DeploymentProcess> processes, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RedownloadSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default) =>
            Task.CompletedTask;

        public bool IsStaticWebInstallerCachedForUrl(DeploymentProcess process) => false;
    }

    private sealed class FakeDownloadDirectoryListingService : IDownloadDirectoryListingService
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

    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        public Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default) => Task.FromResult<AppUpdateInfo?>(null);
        public Task<string?> DownloadUpdateAsync(AppUpdateInfo info, CancellationToken ct = default) => Task.FromResult<string?>(null);
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

    private sealed class FakeLogService : ILogService
    {
        public IReadOnlyList<LogEntry> Entries => Array.Empty<LogEntry>();
        public event EventHandler<LogEntry>? LogAdded { add { } remove { } }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? ex = null) { }
        public void AppendRaw(string level, string message) { }
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void SetText(string text) { }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Dark;
        public AppThemeStyle CurrentThemeStyle => AppThemeStyle.Default;
        public void SetTheme(AppTheme theme) { }
        public void SetThemeStyle(AppThemeStyle style) { }
        public void ToggleTheme() { }
    }

    private sealed class FakeProcessExecutionService : IProcessExecutionService
    {
        public Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public Task<string> Ensure7ZipInstalledAsync(CancellationToken ct = default) =>
            Task.FromResult(string.Empty);

        public Task<string> EnsureUnrarInstalledAsync(CancellationToken ct = default) =>
            Task.FromResult(string.Empty);

        public Task<ProcessResult> RunPowerShellAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public Task<ProcessResult> RunBatchAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));

        public Task<ProcessResult> RunBashAsync(string scriptPathOrContent, bool isInlineScript, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    private sealed class FakeLicenseScraperService : ILicenseScraperService
    {
        public Task<IReadOnlyList<LicenseEntry>> FetchLicensesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LicenseEntry>>(Array.Empty<LicenseEntry>());

        public string? ExtractLicenseKey(IReadOnlyList<LicenseEntry> licenses, string productName, string customerName) => null;
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ShowDisableRequiredWarning(string processName) => true;
        public bool Confirm(string title, string message) => true;
        public IDialogService.UnrarPromptResult ShowUnrarRequiredPrompt(string processName, string details) => IDialogService.UnrarPromptResult.Installa;
        public void ResetDisableRequiredWarningPreference() { }
        public ArgumentPromptResponse ShowArgumentPrompt(string processName, string subtitle, IReadOnlyList<ArgumentInputDefinition> inputs, IReadOnlyDictionary<string, string> prefill) =>
            new(ArgumentPromptChoice.RunOnce, prefill);
    }

    private sealed class FakePresetIconService : IPresetIconService
    {
        public string ImportLightIcon(string presetId, string sourcePath) => sourcePath;
        public string ImportDarkIcon(string presetId, string sourcePath) => sourcePath;
        public void DeletePresetIcons(string presetId) { }
        public IReadOnlyList<PresetIconLibraryItem> GetLibraryIcons() => Array.Empty<PresetIconLibraryItem>();
        public PresetIconLibraryItem ImportLibraryIcon(string sourcePath) => new() { Id = "x", LightPath = sourcePath };
    }
}
