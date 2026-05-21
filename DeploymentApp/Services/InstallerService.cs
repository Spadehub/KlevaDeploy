using System.IO;
using DeploymentApp.Models;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.Services;

public sealed class InstallerService : IInstallerService
{
    private readonly ILogService _log;
    private readonly string _baseDir = AppContext.BaseDirectory;
    private readonly List<DeploymentPreset> _userCreatedPresets = new();
    private IReadOnlyList<DeploymentProcess> _cachedProcesses = Array.Empty<DeploymentProcess>();
    private IReadOnlyList<DeploymentPreset> _cachedPresets = Array.Empty<DeploymentPreset>();

    public InstallerService(ILogService log) => _log = log;

    public Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync(bool isDemoMode)
    {
        _log.Info($"Loading deployment processes (Demo Mode: {isDemoMode})...");
        
        
        if (!isDemoMode)
        {
            // Production mode: return minimal placeholder data
            var productionProcesses = new List<DeploymentProcess>
            {
                new()
                {
                    Id = "vcredist",
                    Name = "Visual C++ Redistributable 2022",
                    Description = "Runtime Microsoft Visual C++ richiesto da molte applicazioni.",
                    Kind = ProcessKind.Installer,
                    RelativePath = @"Installers\vcredist_x64.exe",
                    Arguments = "/install /quiet /norestart",
                    DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                    RequiresAuth = false,
                    RequiresLicense = false,
                    IsRequired = true,
                    EnabledByDefault = true,
                },
                new()
                {
                    Id = "dotnet-runtime",
                    Name = ".NET 8 Desktop Runtime",
                    Description = "Runtime .NET 8 necessario per applicazioni moderne.",
                    Kind = ProcessKind.Installer,
                    RelativePath = @"Installers\dotnet-runtime-8-win-x64.exe",
                    Arguments = "/install /quiet /norestart",
                    DownloadUrl = "https://download.microsoft.com/dotnet/8.0/runtime/dotnet-runtime-8.0-win-x64.exe",
                    RequiresAuth = false,
                    RequiresLicense = false,
                    IsRequired = true,
                    EnabledByDefault = true,
                    DependsOn = new() { "vcredist" },
                },
            };
            _cachedProcesses = productionProcesses;
            return Task.FromResult<IReadOnlyList<DeploymentProcess>>(productionProcesses);
        }
        
        // Demo mode: return full demo data
        var processes = new List<DeploymentProcess>
        {
            new()
            {
                Id = "vcredist",
                Name = "Visual C++ Redistributable 2022",
                Description = "Runtime Microsoft Visual C++ richiesto da molte applicazioni.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\vcredist_x64.exe",
                Arguments = "/install /quiet /norestart",
                DownloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe",
                RequiresAuth = false,
                RequiresLicense = false,
                IsRequired = true,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "dotnet-runtime",
                Name = ".NET 8 Desktop Runtime",
                Description = "Runtime .NET 8 necessario per applicazioni moderne.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\dotnet-runtime-8-win-x64.exe",
                Arguments = "/install /quiet /norestart",
                DownloadUrl = "https://download.microsoft.com/dotnet/8.0/runtime/dotnet-runtime-8.0-win-x64.exe",
                RequiresAuth = false,
                RequiresLicense = false,
                IsRequired = true,
                EnabledByDefault = true,
                DependsOn = new() { "vcredist" },
            },
            new()
            {
                Id = "chrome",
                Name = "Google Chrome",
                Description = "Browser web Google Chrome — versione enterprise.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\ChromeSetup.exe",
                Arguments = "/silent /install",
                DownloadUrl = "",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "office365",
                Name = "Microsoft Office 365",
                Description = "Suite Office 365 con Word, Excel, Outlook, Teams.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\Office365\setup.exe",
                Arguments = "/configure Installers\\Office365\\configuration.xml",
                DownloadUrl = "",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
                DependsOn = new() { "vcredist", "dotnet-runtime" },
            },
            new()
            {
                Id = "passepartout",
                Name = "Passepartout Mexal",
                Description = "ERP gestionale Passepartout Mexal — richiede licenza.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\passepartout_setup.exe",
                Arguments = "/S /LICENSE={LICENSE_KEY}",
                DownloadUrl = "https://www.passepartout.net/downloads/mexal_setup.exe",
                RequiresAuth = true,
                RequiresLicense = true,
                LicenseExcelColumn = "LicenseKey",
                EnabledByDefault = true,
                DependsOn = new() { "vcredist", "dotnet-runtime" },
            },
            new()
            {
                Id = "teamviewer",
                Name = "TeamViewer Host",
                Description = "Accesso remoto per assistenza tecnica.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\TeamViewerHost.exe",
                Arguments = "/S",
                DownloadUrl = "",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "antivirus",
                Name = "Bitdefender Endpoint Security",
                Description = "Antivirus aziendale Bitdefender.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\bitdefender_setup.exe",
                Arguments = "/quiet",
                DownloadUrl = "",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "disable-uac",
                Name = "Disabilita UAC",
                Description = "Imposta UAC al livello minimo tramite registro di sistema.",
                Kind = ProcessKind.RegistryFile,
                RelativePath = @"Scripts\disable_uac.reg",
                Arguments = "/s",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = false,
                IsRequired = false,
            },
            new()
            {
                Id = "firewall-rules",
                Name = "Regole Firewall Standard",
                Description = "Applica regole firewall standard per il laboratorio IT.",
                Kind = ProcessKind.PowerShellScript,
                RelativePath = @"Scripts\firewall_rules.ps1",
                Arguments = "-ExecutionPolicy Bypass -File",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "windows-update",
                Name = "Configura Windows Update",
                Description = "Imposta Windows Update per aggiornamenti automatici notturni.",
                Kind = ProcessKind.PowerShellScript,
                RelativePath = @"Scripts\configure_wu.ps1",
                Arguments = "-ExecutionPolicy Bypass -File",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "printer-setup",
                Name = "Installazione Stampanti",
                Description = "Aggiunge le stampanti di rete del laboratorio.",
                Kind = ProcessKind.PowerShellScript,
                RelativePath = @"Scripts\add_printers.ps1",
                Arguments = "-ExecutionPolicy Bypass -File",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = false,
            },
            new()
            {
                Id = "7zip",
                Name = "7-Zip",
                Description = "Utility di compressione file 7-Zip.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\7z2301-x64.exe",
                Arguments = "/S",
                DownloadUrl = "",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = true,
            },
            new()
            {
                Id = "notepadpp",
                Name = "Notepad++",
                Description = "Editor di testo avanzato Notepad++.",
                Kind = ProcessKind.Installer,
                RelativePath = @"Installers\npp.installer.exe",
                Arguments = "/S",
                DownloadUrl = "",
                RequiresAuth = false,
                RequiresLicense = false,
                EnabledByDefault = false,
            },
        };
        _cachedProcesses = processes;
        return Task.FromResult<IReadOnlyList<DeploymentProcess>>(processes);
    }

    public Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync(bool isDemoMode)
    {
        _log.Info($"Loading deployment presets (Demo Mode: {isDemoMode})...");
        
        if (!isDemoMode)
        {
            // Production mode: return minimal placeholder data
            var productionPresets = new List<DeploymentPreset>
            {
                new()
                {
                    Id = "base-workstation",
                    Name = "Postazione Base",
                    Description = "Setup minimo per qualsiasi postazione: runtime, browser.",
                    Category = "Generale",
                    Icon = "🖥️",
                    Steps = new()
                    {
                        new() { ProcessId = "vcredist",       Order = 10 },
                        new() { ProcessId = "dotnet-runtime", Order = 20 },
                    }
                },
            };
            _cachedPresets = productionPresets;
            return Task.FromResult<IReadOnlyList<DeploymentPreset>>(productionPresets);
        }
        
        // Demo mode: return full demo data
        var presets = new List<DeploymentPreset>
        {
            new()
            {
                Id = "base-workstation",
                Name = "Postazione Base",
                Description = "Setup minimo per qualsiasi postazione: runtime, browser, antivirus, firewall.",
                Category = "Generale",
                Icon = "🖥️",
                Steps = new()
                {
                    new() { ProcessId = "vcredist",       Order = 10 },
                    new() { ProcessId = "dotnet-runtime", Order = 20 },
                    new() { ProcessId = "7zip",           Order = 30 },
                    new() { ProcessId = "chrome",         Order = 40 },
                    new() { ProcessId = "antivirus",      Order = 50 },
                    new() { ProcessId = "firewall-rules", Order = 60 },
                    new() { ProcessId = "windows-update", Order = 70 },
                    new() { ProcessId = "teamviewer",     Order = 80 },
                }
            },
            new()
            {
                Id = "accounting",
                Name = "Postazione Contabilità",
                Description = "Postazione completa per ufficio contabilità con Passepartout Mexal e Office.",
                Category = "Ufficio",
                Icon = "📊",
                Steps = new()
                {
                    new() { ProcessId = "vcredist",       Order = 10 },
                    new() { ProcessId = "dotnet-runtime", Order = 20 },
                    new() { ProcessId = "7zip",           Order = 30 },
                    new() { ProcessId = "chrome",         Order = 40 },
                    new() { ProcessId = "office365",      Order = 50 },
                    new() { ProcessId = "passepartout",   Order = 60 },
                    new() { ProcessId = "antivirus",      Order = 70 },
                    new() { ProcessId = "firewall-rules", Order = 80 },
                    new() { ProcessId = "windows-update", Order = 90 },
                    new() { ProcessId = "teamviewer",     Order = 100 },
                    new() { ProcessId = "printer-setup",  Order = 110 },
                }
            },
            new()
            {
                Id = "developer",
                Name = "Postazione Sviluppatore",
                Description = "Workstation per sviluppatori con strumenti di sviluppo e utilità.",
                Category = "Tecnico",
                Icon = "💻",
                Steps = new()
                {
                    new() { ProcessId = "vcredist",       Order = 10 },
                    new() { ProcessId = "dotnet-runtime", Order = 20 },
                    new() { ProcessId = "7zip",           Order = 30 },
                    new() { ProcessId = "chrome",         Order = 40 },
                    new() { ProcessId = "notepadpp",      Order = 50 },
                    new() { ProcessId = "antivirus",      Order = 60 },
                    new() { ProcessId = "firewall-rules", Order = 70 },
                    new() { ProcessId = "windows-update", Order = 80 },
                    new() { ProcessId = "teamviewer",     Order = 90 },
                }
            },
            new()
            {
                Id = "reception",
                Name = "Postazione Reception",
                Description = "Postazione reception con Office, browser e stampanti.",
                Category = "Ufficio",
                Icon = "🏢",
                Steps = new()
                {
                    new() { ProcessId = "vcredist",       Order = 10 },
                    new() { ProcessId = "dotnet-runtime", Order = 20 },
                    new() { ProcessId = "chrome",         Order = 30 },
                    new() { ProcessId = "office365",      Order = 40 },
                    new() { ProcessId = "antivirus",      Order = 50 },
                    new() { ProcessId = "firewall-rules", Order = 60 },
                    new() { ProcessId = "windows-update", Order = 70 },
                    new() { ProcessId = "printer-setup",  Order = 80 },
                    new() { ProcessId = "teamviewer",     Order = 90 },
                }
            },
            new()
            {
                Id = "server-config",
                Name = "Configurazione Server",
                Description = "Script di configurazione per server Windows: firewall, UAC, aggiornamenti.",
                Category = "Server",
                Icon = "🖧",
                Steps = new()
                {
                    new() { ProcessId = "vcredist",       Order = 10 },
                    new() { ProcessId = "dotnet-runtime", Order = 20 },
                    new() { ProcessId = "disable-uac",    Order = 30 },
                    new() { ProcessId = "firewall-rules", Order = 40 },
                    new() { ProcessId = "windows-update", Order = 50 },
                    new() { ProcessId = "teamviewer",     Order = 60 },
                    new() { ProcessId = "antivirus",      Order = 70 },
                }
            },
        };
        _cachedPresets = presets;
        return Task.FromResult<IReadOnlyList<DeploymentPreset>>(presets);
    }

    public IReadOnlyList<(DeploymentProcess Process, int Order, bool IsRequired)> BuildExecutionQueue(
        IEnumerable<DeploymentPreset> selectedPresets,
        IReadOnlyList<DeploymentProcess> allProcesses)
    {
        var processMap = allProcesses.ToDictionary(p => p.Id);
        // Collect all steps from all selected presets; if a process appears multiple times, keep lowest Order
        // IsRequired is true if ANY preset marks it as required
        var merged = new Dictionary<string, (int Order, bool IsRequired)>();
        foreach (var preset in selectedPresets)
        {
            foreach (var step in preset.Steps)
            {
                if (!merged.TryGetValue(step.ProcessId, out var existing))
                {
                    merged[step.ProcessId] = (step.Order, step.IsRequired);
                }
                else
                {
                    // Keep lowest order, but IsRequired is true if ANY preset marks it as required
                    var newOrder = step.Order < existing.Order ? step.Order : existing.Order;
                    var isRequired = existing.IsRequired || step.IsRequired;
                    merged[step.ProcessId] = (newOrder, isRequired);
                }
            }
        }
        return merged
            .Where(kv => processMap.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Value.Order)
            .Select(kv => (processMap[kv.Key], kv.Value.Order, kv.Value.IsRequired))
            .ToList();
    }

    public string ResolveProcessPath(DeploymentProcess process) =>
        Path.Combine(_baseDir, process.RelativePath);

    public void AddUserPreset(DeploymentPreset preset)
    {
        _userCreatedPresets.Add(preset);
        _log.Info($"User created preset: {preset.Name} with {preset.Steps.Count} steps.");
    }

    public IReadOnlyList<DeploymentProcess> GetAllAvailableProcesses()
    {
        return _cachedProcesses;
    }

    public IReadOnlyList<DeploymentPreset> GetAllPresets()
    {
        // Combine cached presets with user-created presets
        return _cachedPresets.Concat(_userCreatedPresets).ToList();
    }
}
