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
        var log = new FakeLogService();
        var theme = new FakeThemeService();
        var dialog = new FakeDialogService();
        var presetIcon = new FakePresetIconService();
        var logVm = new LogViewModel(log);

        return new MainViewModel(
            installer,
            update,
            auth,
            log,
            theme,
            dialog,
            presetIcon,
            prefsService,
            loginVmFactory: () => new LoginViewModel(auth),
            logVm);
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
    }

    private sealed class FakeUpdateService : IUpdateService
    {
        public Task CheckAndUpdateAsync(IReadOnlyList<SoftwarePackage> packages, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuthService : IAuthService
    {
        public bool IsAuthenticated => false;
        public Task<bool> LoginAsync(string username, string password, CancellationToken ct = default) => Task.FromResult(false);
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

    private sealed class FakeThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Dark;
        public void SetTheme(AppTheme theme) { }
        public void ToggleTheme() { }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ShowDisableRequiredWarning(string processName) => true;
        public void ResetDisableRequiredWarningPreference() { }
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
