using System.Reflection;
using KlevaDeploy.Models;
using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Tests;

public sealed class InstallerExecutionRegressionTests
{
    [Fact]
    public void InstallerService_NormalizeKnownScriptFixups_FixesBrokenSqlPassInlineScript()
    {
        var process = new DeploymentProcess
        {
            Id = "sql-express-2022-sqlpass",
            Name = "SQL Server Express 2022 (SQLPASS)",
            Kind = ProcessKind.Installer,
            SubProcesses =
            {
                new DeploymentSubProcess
                {
                    Name = "Install SQLPASS",
                    Process = new DeploymentProcess
                    {
                        Name = "Install SQLPASS",
                        Kind = ProcessKind.PowerShellScript,
                        ScriptContent = "$saPwd = $env:KLEVADEPLOY_SQLPASS_SA_PASSWORD`nif ([string]::IsNullOrWhiteSpace($saPwd)) {`n  $saPwdthrowthrow 'KLEVADEPLOY_SQLPASS_SA_PASSWORD non impostata.'`n}"
                    }
                }
            }
        };

        var method = typeof(InstallerService).GetMethod("NormalizeKnownScriptFixups", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var changed = (bool)method!.Invoke(null, [process])!;
        Assert.True(changed);

        var script = process.SubProcesses[0].Process!.ScriptContent;
        Assert.DoesNotContain("$saPwdthrowthrow", script, StringComparison.Ordinal);
        Assert.Contains("throw 'KLEVADEPLOY_SQLPASS_SA_PASSWORD non impostata.'", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunSingleProcessHeadless_DownloadsParentInstallerBeforeRunningSubProcesses_AndDoesNotRunParentExe()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KlevaDeployTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var installerPath = Path.Combine(tempDir, "parent-installer.exe");
            var process = new DeploymentProcess
            {
                Id = "web-parent",
                Name = "Web Parent",
                Kind = ProcessKind.Installer,
                InstallerSourceMode = InstallerSourceMode.StaticWeb,
                DownloadUrl = "https://example.invalid/parent-installer.exe",
                RelativePath = installerPath,
                SubProcesses =
                [
                    new DeploymentSubProcess
                    {
                        Name = "Child script",
                        Process = new DeploymentProcess
                        {
                            Id = "child-script",
                            Name = "Child script",
                            Kind = ProcessKind.PowerShellScript,
                            ScriptContent = "Write-Output 'ok'"
                        }
                    }
                ]
            };

            var installer = new SingleProcessInstallerService(process);
            var update = new RecordingUpdateService(installerPath);
            var execution = new RecordingProcessExecutionService(installerPath);
            var log = new FakeLogService();
            var prefs = new FakePreferencesService(new UserPreferences());
            var vm = new MainViewModel(
                installer,
                update,
                new FakeAuthService(),
                new FakeDownloadDirectoryListingService(),
                new FakeAppUpdateService(),
                execution,
                new FakeLicenseScraperService(),
                log,
                new FakeThemeService(),
                new FakeDialogService(),
                new FakePresetIconService(),
                prefs,
                loginVmFactory: () => new LoginViewModel(new FakeAuthService(), prefs),
                logViewModel: new LogViewModel(log, new FakeClipboardService()));

            var exitCode = await vm.RunSingleProcessHeadlessAsync(process.Id);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(installerPath));
            Assert.Equal(["update-start", "update-done", "child-powershell"], update.Events.Concat(execution.Events).ToArray());
            Assert.False(execution.ParentInstallerRunAttempted);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RunSingleProcessHeadless_PromptsForRequiredEnvVarUsedBySubProcessScript()
    {
        var process = new DeploymentProcess
        {
            Id = "sql-parent",
            Name = "SQL Parent",
            Kind = ProcessKind.PowerShellScript,
            SubProcesses =
            [
                new DeploymentSubProcess
                {
                    Name = "Install SQLPASS",
                    Process = new DeploymentProcess
                    {
                        Id = "sql-child",
                        Name = "Install SQLPASS",
                        Kind = ProcessKind.PowerShellScript,
                        ScriptContent = "$saPwd = $env:KLEVADEPLOY_SQLPASS_SA_PASSWORD`nif ([string]::IsNullOrWhiteSpace($saPwd)) { throw 'missing' }"
                    }
                }
            ]
        };

        var installer = new SingleProcessInstallerService(process);
        string promptedProcessName = string.Empty;
        IReadOnlyList<ArgumentInputDefinition> promptedInputs = Array.Empty<ArgumentInputDefinition>();
        var dialog = new FakeDialogService
        {
            ArgumentPromptHandler = (processName, _, inputs, _) =>
            {
                promptedProcessName = processName;
                promptedInputs = inputs.ToList();
                return new ArgumentPromptResponse(
                    ArgumentPromptChoice.RunOnce,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["KLEVADEPLOY_SQLPASS_SA_PASSWORD"] = "Secret123!"
                    });
            }
        };

        var execution = new RecordingProcessExecutionService(null)
        {
            PowerShellHandler = _ =>
            {
                Assert.Equal("Secret123!", Environment.GetEnvironmentVariable("KLEVADEPLOY_SQLPASS_SA_PASSWORD"));
                return new ProcessResult(0, string.Empty, string.Empty);
            }
        };

        var log = new FakeLogService();
        var prefs = new FakePreferencesService(new UserPreferences());
        var vm = new MainViewModel(
            installer,
            new RecordingUpdateService(null),
            new FakeAuthService(),
            new FakeDownloadDirectoryListingService(),
            new FakeAppUpdateService(),
            execution,
            new FakeLicenseScraperService(),
            log,
            new FakeThemeService(),
            dialog,
            new FakePresetIconService(),
            prefs,
            loginVmFactory: () => new LoginViewModel(new FakeAuthService(), prefs),
            logViewModel: new LogViewModel(log, new FakeClipboardService()));

        var exitCode = await vm.RunSingleProcessHeadlessAsync(process.Id);

        Assert.Equal(0, exitCode);
        Assert.Equal("SQL Parent", promptedProcessName);
        Assert.Equal(1, dialog.ArgumentPromptCount);
        Assert.Contains(promptedInputs, x => string.Equals(x.Key, "KLEVADEPLOY_SQLPASS_SA_PASSWORD", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("child-powershell", execution.Events);
    }

    [Fact]
    public async Task RunSingleProcessHeadless_PromptPrefill_RestoresNonSecretDefaults_WhenProfileContainsEmptyValues()
    {
        var process = new DeploymentProcess
        {
            Id = "retail-server",
            Name = "Passepartout Retail Server",
            Kind = ProcessKind.PowerShellScript,
            ScriptContent = "Write-Output 'ok'",
            ArgumentInputs =
            [
                new ArgumentInputDefinition
                {
                    Key = "KLEVADEPLOY_RETAIL_SQL_SERVER",
                    Label = "Server SQL",
                    DefaultValue = "%COMPUTERNAME%\\SQLPASS",
                    IsRequired = true
                },
                new ArgumentInputDefinition
                {
                    Key = "KLEVADEPLOY_RETAIL_DB_NAME",
                    Label = "Nome database",
                    DefaultValue = "PassepartoutRetail",
                    IsRequired = true
                },
                new ArgumentInputDefinition
                {
                    Key = "KLEVADEPLOY_SQLPASS_SA_PASSWORD",
                    Label = "Password SQL (sa)",
                    DefaultValue = "",
                    IsSecret = true,
                    IsRequired = true
                }
            ]
        };

        IReadOnlyDictionary<string, string> promptedPrefill = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dialog = new FakeDialogService
        {
            ArgumentPromptHandler = (_, _, _, prefill) =>
            {
                promptedPrefill = new Dictionary<string, string>(prefill, StringComparer.OrdinalIgnoreCase);
                return new ArgumentPromptResponse(
                    ArgumentPromptChoice.RunOnce,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["KLEVADEPLOY_RETAIL_SQL_SERVER"] = prefill["KLEVADEPLOY_RETAIL_SQL_SERVER"],
                        ["KLEVADEPLOY_RETAIL_DB_NAME"] = prefill["KLEVADEPLOY_RETAIL_DB_NAME"],
                        ["KLEVADEPLOY_SQLPASS_SA_PASSWORD"] = string.Empty
                    });
            }
        };

        var prefs = new FakePreferencesService(new UserPreferences
        {
            ProcessArgumentProfiles =
            [
                new ProcessArgumentProfile
                {
                    ProcessId = process.Id,
                    SchemaHash = string.Empty,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["KLEVADEPLOY_RETAIL_SQL_SERVER"] = string.Empty,
                        ["KLEVADEPLOY_RETAIL_DB_NAME"] = string.Empty,
                        ["KLEVADEPLOY_SQLPASS_SA_PASSWORD"] = string.Empty
                    }
                }
            ]
        });

        var installer = new SingleProcessInstallerService(process);
        var log = new FakeLogService();
        var vm = new MainViewModel(
            installer,
            new RecordingUpdateService(null),
            new FakeAuthService(),
            new FakeDownloadDirectoryListingService(),
            new FakeAppUpdateService(),
            new RecordingProcessExecutionService(null),
            new FakeLicenseScraperService(),
            log,
            new FakeThemeService(),
            dialog,
            new FakePresetIconService(),
            prefs,
            loginVmFactory: () => new LoginViewModel(new FakeAuthService(), prefs),
            logViewModel: new LogViewModel(log, new FakeClipboardService()));

        var exitCode = await vm.RunSingleProcessHeadlessAsync(process.Id);

        Assert.Equal(0, exitCode);
        Assert.Equal("%COMPUTERNAME%\\SQLPASS", promptedPrefill["KLEVADEPLOY_RETAIL_SQL_SERVER"]);
        Assert.Equal("PassepartoutRetail", promptedPrefill["KLEVADEPLOY_RETAIL_DB_NAME"]);
        Assert.Equal(string.Empty, promptedPrefill["KLEVADEPLOY_SQLPASS_SA_PASSWORD"]);
    }

    private sealed class SingleProcessInstallerService(DeploymentProcess process) : IInstallerService
    {
        private readonly IReadOnlyList<DeploymentProcess> _processes = [process];
        public Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync() => Task.FromResult<IReadOnlyList<DeploymentPreset>>(Array.Empty<DeploymentPreset>());
        public Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync() => Task.FromResult(_processes);
        public IReadOnlyList<(DeploymentProcess Process, int Order, bool IsRequired)> BuildExecutionQueue(IEnumerable<DeploymentPreset> selectedPresets, IReadOnlyList<DeploymentProcess> allProcesses) => Array.Empty<(DeploymentProcess, int, bool)>();
        public string ResolveProcessPath(DeploymentProcess process) => process.RelativePath;
        public void AddUserPreset(DeploymentPreset preset) { }
        public IReadOnlyList<DeploymentProcess> GetAllAvailableProcesses() => _processes;
        public IReadOnlyList<DeploymentPreset> GetAllPresets() => Array.Empty<DeploymentPreset>();
        public void AddUserProcess(DeploymentProcess process) { }
        public void UpdatePreset(DeploymentPreset preset) { }
        public void UpdateProcess(DeploymentProcess process) { }
        public bool DeletePreset(string presetId) => false;
        public bool DeleteProcess(string processId) => false;
    }

    private sealed class RecordingUpdateService(string? installerPath) : IUpdateService
    {
        public List<string> Events { get; } = [];
        public Task CheckAndUpdateInstallersAsync(IReadOnlyList<DeploymentProcess> processes, CancellationToken ct = default) => Task.CompletedTask;
        public bool IsStaticWebInstallerCachedForUrl(DeploymentProcess process) => !string.IsNullOrWhiteSpace(installerPath) && File.Exists(installerPath);
        public Task RedownloadSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default) => UpdateSingleInstallerAsync(process, ct);

        public async Task UpdateSingleInstallerAsync(DeploymentProcess process, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(installerPath))
                return;

            Events.Add("update-start");
            await Task.Delay(20, ct);
            await File.WriteAllTextAsync(installerPath, "stub", ct);
            Events.Add("update-done");
        }
    }

    private sealed class RecordingProcessExecutionService(string? installerPath) : IProcessExecutionService
    {
        public List<string> Events { get; } = [];
        public bool ParentInstallerRunAttempted { get; private set; }
        public Func<string, ProcessResult>? PowerShellHandler { get; init; }

        public Task<string> Ensure7ZipInstalledAsync(CancellationToken ct = default) => Task.FromResult(string.Empty);
        public Task<string> EnsureUnrarInstalledAsync(CancellationToken ct = default) => Task.FromResult(string.Empty);

        public Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(installerPath) &&
                string.Equals(executablePath, installerPath, StringComparison.OrdinalIgnoreCase))
                ParentInstallerRunAttempted = true;

            Events.Add("run-async");
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }

        public Task<ProcessResult> RunPowerShellAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default)
        {
            Events.Add("child-powershell");
            if (!string.IsNullOrWhiteSpace(installerPath))
                Assert.True(File.Exists(installerPath), "The parent installer should exist before the subprocess runs.");
            return Task.FromResult(PowerShellHandler?.Invoke(scriptPathOrContent) ?? new ProcessResult(0, string.Empty, string.Empty));
        }

        public Task<ProcessResult> RunBatchAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default) => Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        public Task<ProcessResult> RunBashAsync(string scriptPathOrContent, bool isInlineScript, CancellationToken ct = default) => Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
    }

    private sealed class FakePreferencesService(UserPreferences prefs) : IPreferencesService
    {
        public UserPreferences Preferences { get; } = prefs;
        public void Save() { }
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

    private sealed class FakeDownloadDirectoryListingService : IDownloadDirectoryListingService
    {
        public Task<LatestFolderExeListing?> GetLatestFolderExeListingAsync(string baseFolderUrl, bool pickLatestFolderByName, CancellationToken ct = default) => Task.FromResult<LatestFolderExeListing?>(null);
        public Task<IReadOnlyList<string>> ListSubfoldersAsync(string baseFolderUrl, bool pickLatestFolderByName, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<LatestFolderExeListing?> GetFolderExeListingAsync(string folderUrl, CancellationToken ct = default) => Task.FromResult<LatestFolderExeListing?>(null);
        public Task<string?> ResolveDownloadUrlAsync(string baseFolderUrl, bool pickLatestFolderByName, string selectedFileTemplate, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<string?> ResolveDownloadUrlAsync(string baseFolderUrl, bool pickLatestFolderByName, string selectedFileTemplate, string? versionFolderName, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeAppUpdateService : IAppUpdateService
    {
        public Task<AppUpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default) => Task.FromResult<AppUpdateInfo?>(null);
        public Task<string?> DownloadUpdateAsync(AppUpdateInfo info, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public string? LaunchUpdater(string downloadedUpdatePath) => null;
    }

    private sealed class FakeLicenseScraperService : ILicenseScraperService
    {
        public Task<IReadOnlyList<LicenseEntry>> FetchLicensesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LicenseEntry>>(Array.Empty<LicenseEntry>());
        public string? ExtractLicenseKey(IReadOnlyList<LicenseEntry> licenses, string productName, string customerName) => null;
    }

    private sealed class FakeThemeService : IThemeService
    {
        public AppTheme CurrentTheme => AppTheme.Dark;
        public AppThemeStyle CurrentThemeStyle => AppThemeStyle.Default;
        public void SetTheme(AppTheme theme) { }
        public void SetThemeStyle(AppThemeStyle style) { }
        public void ToggleTheme() { }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Func<string, string, IReadOnlyList<ArgumentInputDefinition>, IReadOnlyDictionary<string, string>, ArgumentPromptResponse>? ArgumentPromptHandler { get; init; }
        public int ArgumentPromptCount { get; private set; }

        public bool ShowDisableRequiredWarning(string processName) => true;
        public bool Confirm(string title, string message) => true;
        public IDialogService.UnrarPromptResult ShowUnrarRequiredPrompt(string processName, string details) => IDialogService.UnrarPromptResult.Installa;
        public void ResetDisableRequiredWarningPreference() { }
        public ArgumentPromptResponse ShowArgumentPrompt(string processName, string subtitle, IReadOnlyList<ArgumentInputDefinition> inputs, IReadOnlyDictionary<string, string> prefill)
        {
            ArgumentPromptCount++;
            return ArgumentPromptHandler?.Invoke(processName, subtitle, inputs, prefill) ?? new ArgumentPromptResponse(ArgumentPromptChoice.RunOnce, prefill);
        }
    }

    private sealed class FakePresetIconService : IPresetIconService
    {
        public string ImportLightIcon(string presetId, string sourcePath) => sourcePath;
        public string ImportDarkIcon(string presetId, string sourcePath) => sourcePath;
        public void DeletePresetIcons(string presetId) { }
        public IReadOnlyList<PresetIconLibraryItem> GetLibraryIcons() => Array.Empty<PresetIconLibraryItem>();
        public PresetIconLibraryItem ImportLibraryIcon(string sourcePath) => new() { Id = "x", LightPath = sourcePath };
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public void SetText(string text) { }
    }
}
