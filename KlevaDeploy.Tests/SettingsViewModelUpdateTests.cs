using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Tests;

public sealed class SettingsViewModelUpdateTests
{
    [Fact]
    public async Task CheckThenInstallUpdate_PopulatesReleaseDetailsAndLaunchesUpdater()
    {
        var updateInfo = new AppUpdateInfo(
            "0.3.0",
            "https://github.com/Spadehub/KlevaDeploy/releases/download/v0.3.0/KlevaDeploy.exe",
            "KlevaDeploy.exe",
            "v0.3.0",
            "Important fixes",
            "https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.3.0",
            false,
            new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero),
            123456);

        var appUpdateService = new FakeAppUpdateService
        {
            AvailableUpdate = updateInfo,
            DownloadedPath = @"C:\Temp\KlevaDeploy-0.3.0.exe"
        };
        var shutdownRequested = false;
        var vm = new SettingsViewModel(
            appUpdateService,
            new FakePreferencesService(),
            new FakeThemeService(),
            new FakeLogService(),
            new FakeDialogService(),
            new FakePresetIconService(),
            requestAppShutdown: () => shutdownRequested = true);

        await vm.CheckForUpdateCommand.ExecuteAsync(null);

        Assert.True(vm.IsUpdateAvailable);
        Assert.Equal("0.3.0", vm.AvailableVersion);
        Assert.Equal("v0.3.0", vm.UpdateReleaseName);
        Assert.Equal("Important fixes", vm.UpdateReleaseNotes);
        Assert.Equal("https://github.com/Spadehub/KlevaDeploy/releases/tag/v0.3.0", vm.UpdateReleasePageUrl);

        await vm.DownloadAndInstallUpdateCommand.ExecuteAsync(null);

        Assert.True(appUpdateService.DownloadCalled);
        Assert.True(appUpdateService.LaunchCalled);
        Assert.Equal(@"C:\Temp\KlevaDeploy-0.3.0.exe", appUpdateService.LaunchedPath);
        Assert.True(shutdownRequested);
        Assert.Contains("Riavvio in corso", vm.UpdateStatusText);
    }

    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        public AppUpdateInfo? AvailableUpdate { get; set; }
        public string? DownloadedPath { get; set; }
        public bool DownloadCalled { get; private set; }
        public bool LaunchCalled { get; private set; }
        public string? LaunchedPath { get; private set; }

        public Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default) =>
            Task.FromResult(AvailableUpdate);

        public Task<string?> DownloadUpdateAsync(AppUpdateInfo info, CancellationToken ct = default)
        {
            DownloadCalled = true;
            return Task.FromResult(DownloadedPath);
        }

        public string? LaunchUpdater(string downloadedUpdatePath)
        {
            LaunchCalled = true;
            LaunchedPath = downloadedUpdatePath;
            return null;
        }
    }

    private sealed class FakePreferencesService : IPreferencesService
    {
        public UserPreferences Preferences { get; } = new();
        public void Save() { }
    }

    private sealed class FakeThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Dark;
        public AppThemeStyle CurrentThemeStyle => AppThemeStyle.Default;
        public void SetTheme(AppTheme theme) { }
        public void SetThemeStyle(AppThemeStyle style) { }
        public void ToggleTheme() { }
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
        public PresetIconLibraryItem ImportLibraryIcon(string sourcePath) => new() { Id = "icon", LightPath = sourcePath };
    }
}
