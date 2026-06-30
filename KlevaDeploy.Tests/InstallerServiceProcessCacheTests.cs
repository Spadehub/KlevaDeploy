using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KlevaDeploy.Models;
using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;
using Xunit;

namespace KlevaDeploy.Tests;

[Collection("EnvVar")]
public sealed class InstallerServiceProcessCacheTests
{
    [Fact]
    public async Task AddUserProcess_UpdatesGetAllAvailableProcesses()
    {
        var (svc, cleanup) = CreateServiceWithTempStorage();
        try
        {
            await svc.LoadProcessesAsync();

            svc.AddUserProcess(new DeploymentProcess
            {
                Id = "custom-1",
                Name = "Custom Process 1",
                Description = "Custom",
                Kind = ProcessKind.PowerShellScript,
                RelativePath = @"Scripts\custom1.ps1",
                Arguments = "-ExecutionPolicy Bypass -File",
                EnabledByDefault = false,
            });

            var all = svc.GetAllAvailableProcesses();
            Assert.Contains(all, p => p.Id == "custom-1");
            Assert.Equal(1, all.Count(p => p.Id == "custom-1"));
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task UpdateProcess_UpdatesExistingCustomProcessWithoutDuplicatingId()
    {
        var (svc, cleanup) = CreateServiceWithTempStorage();
        try
        {
            await svc.LoadProcessesAsync();

            svc.AddUserProcess(new DeploymentProcess
            {
                Id = "chrome",
                Name = "Google Chrome",
                Description = "Initial",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\ChromeSetup.exe",
                Arguments = "/silent /install",
                EnabledByDefault = true,
            });

            svc.UpdateProcess(new DeploymentProcess
            {
                Id = "chrome",
                Name = "Google Chrome (Custom Override)",
                Description = "Overridden",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\ChromeSetup.exe",
                Arguments = "/silent /install",
                EnabledByDefault = true,
            });

            var all = svc.GetAllAvailableProcesses();
            Assert.Equal(1, all.Count(p => p.Id == "chrome"));
            Assert.Equal("Google Chrome (Custom Override)", all.Single(p => p.Id == "chrome").Name);
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task DeleteProcess_RemovesCustomProcess()
    {
        var (svc, cleanup) = CreateServiceWithTempStorage();
        try
        {
            await svc.LoadProcessesAsync();

            svc.AddUserProcess(new DeploymentProcess
            {
                Id = "custom-del",
                Name = "Custom Delete",
                Description = "Custom",
                Kind = ProcessKind.PowerShellScript,
                RelativePath = @"Scripts\custom_del.ps1",
                Arguments = "-ExecutionPolicy Bypass -File",
                EnabledByDefault = false,
            });

            Assert.Contains(svc.GetAllAvailableProcesses(), p => p.Id == "custom-del");
            Assert.True(svc.DeleteProcess("custom-del"));
            Assert.DoesNotContain(svc.GetAllAvailableProcesses(), p => p.Id == "custom-del");
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task DeletePreset_RemovesCustomPreset()
    {
        var (svc, cleanup) = CreateServiceWithTempStorage();
        try
        {
            await svc.LoadPresetsAsync();

            svc.AddUserPreset(new DeploymentPreset
            {
                Id = "custom-preset",
                Name = "Custom Preset",
                Category = "Personalizzato",
                Description = "x",
                Steps = new() { new PresetProcessStep { ProcessId = "chrome", Order = 10 } }
            });

            Assert.Contains(svc.GetAllPresets(), p => p.Id == "custom-preset");
            Assert.True(svc.DeletePreset("custom-preset"));
            Assert.DoesNotContain(svc.GetAllPresets(), p => p.Id == "custom-preset");
        }
        finally
        {
            cleanup();
        }
    }

    [Fact]
    public async Task LoadPresetsAsync_ReturnsCustomPresetsAfterTheyAreAdded()
    {
        var (svc, cleanup) = CreateServiceWithTempStorage();
        try
        {
            await svc.LoadPresetsAsync();

            svc.AddUserPreset(new DeploymentPreset
            {
                Id = "imported-package",
                Name = "Imported Package",
                Category = "Test",
                Description = "Imported from bundle",
                Steps = new() { new PresetProcessStep { ProcessId = "retail-server", Order = 10 } }
            });

            var loaded = await svc.LoadPresetsAsync();

            Assert.Contains(loaded, p => p.Id == "imported-package");
            Assert.Equal(1, loaded.Count(p => p.Id == "imported-package"));
        }
        finally
        {
            cleanup();
        }
    }

    private static (InstallerService Svc, Action Cleanup) CreateServiceWithTempStorage()
    {
        var original = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        var tempDir = Path.Combine(Path.GetTempPath(), "KlevaDeploy.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        Environment.SetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR", tempDir);

        var svc = new InstallerService(new FakeLogService(), new FakeConfigService());

        return (svc, () =>
        {
            Environment.SetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR", original);
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
            }
        });
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

    private sealed class FakeConfigService : IAppConfigService
    {
        public AppConfig Config { get; } = new();
    }
}

