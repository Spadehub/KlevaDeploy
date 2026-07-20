using System.Text.Json;
using System.IO;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class InstallerService : IInstallerService
{
    private readonly ILogService _log;
    private readonly IAppConfigService _config;
    private readonly string _baseDir = AppContext.BaseDirectory;
    private readonly string _storageDir;
    private readonly string _presetsFilePath;
    private readonly string _processesFilePath;
    private readonly string _libraryResetMarkerPath;
    
    private List<DeploymentPreset> _userCreatedPresets = new();
    private List<DeploymentProcess> _userCreatedProcesses = new();
    private List<DeploymentProcess> _baseProcesses = new();
    private IReadOnlyList<DeploymentProcess> _cachedProcesses = Array.Empty<DeploymentProcess>();
    private IReadOnlyList<DeploymentPreset> _cachedPresets = Array.Empty<DeploymentPreset>();

    public InstallerService(ILogService log, IAppConfigService config)
    {
        _log = log;
        _config = config;
        _storageDir = GetStorageDir();
        _presetsFilePath = Path.Combine(_storageDir, "custom_presets.json");
        _processesFilePath = Path.Combine(_storageDir, "custom_processes.json");
        _libraryResetMarkerPath = Path.Combine(_storageDir, ".library_reset_done");
        
        Directory.CreateDirectory(_storageDir);
        TryResetLibrary();
        LoadUserStorage();
    }

    private static string GetStorageDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    private void LoadUserStorage()
    {
        try
        {
            var needsSave = false;

            if (File.Exists(_processesFilePath))
            {
                var json = File.ReadAllText(_processesFilePath);
                _userCreatedProcesses = JsonSerializer.Deserialize<List<DeploymentProcess>>(json) ?? new();
                needsSave |= NormalizeProcessIcons(_userCreatedProcesses);
                needsSave |= NormalizeInstallerProcesses(_userCreatedProcesses);
                _log.Info($"Loaded {_userCreatedProcesses.Count} custom processes from storage.");
            }
            
            if (File.Exists(_presetsFilePath))
            {
                var json = File.ReadAllText(_presetsFilePath);
                _userCreatedPresets = JsonSerializer.Deserialize<List<DeploymentPreset>>(json) ?? new();
                _log.Info($"Loaded {_userCreatedPresets.Count} custom packages from storage.");
            }

            if (needsSave)
                SaveUserStorage();
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load user storage", ex);
        }
    }

    private void SaveUserStorage()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            
            var processesJson = JsonSerializer.Serialize(_userCreatedProcesses, options);
            File.WriteAllText(_processesFilePath, processesJson);
            
            var presetsJson = JsonSerializer.Serialize(_userCreatedPresets, options);
            File.WriteAllText(_presetsFilePath, presetsJson);
            
            _log.Info("Custom packages and processes saved to storage.");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save user storage", ex);
        }
    }

    private void TryResetLibrary()
    {
        try
        {
            if (File.Exists(_libraryResetMarkerPath)) return;

            var now = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var archivedAny = false;

            if (File.Exists(_processesFilePath))
            {
                var dest = Path.Combine(_storageDir, $"custom_processes.bak_{now}.json");
                File.Move(_processesFilePath, dest, overwrite: true);
                archivedAny = true;
            }
            if (File.Exists(_presetsFilePath))
            {
                var dest = Path.Combine(_storageDir, $"custom_presets.bak_{now}.json");
                File.Move(_presetsFilePath, dest, overwrite: true);
                archivedAny = true;
            }

            _userCreatedProcesses = new List<DeploymentProcess>();
            _userCreatedPresets = new List<DeploymentPreset>();
            RebuildProcessCache();
            RebuildPresetCache();
            SaveUserStorage();
            File.WriteAllText(_libraryResetMarkerPath, archivedAny ? "archived" : "empty");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to reset library", ex);
        }
    }

    public Task<IReadOnlyList<DeploymentProcess>> LoadProcessesAsync()
    {
        _log.Info("Loading deployment processes...");
        _baseProcesses = new List<DeploymentProcess>();
        RebuildProcessCache();
        return Task.FromResult(_cachedProcesses);
    }

    public Task<IReadOnlyList<DeploymentPreset>> LoadPresetsAsync()
    {
        _log.Info("Loading deployment packages...");
        RebuildPresetCache();
        return Task.FromResult(_cachedPresets);
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
        _log.Info($"User created package: {preset.Name} with {preset.Steps.Count} steps.");
        RebuildPresetCache();
        SaveUserStorage();
    }

    public void AddUserProcess(DeploymentProcess process)
    {
        var existingIndex = _userCreatedProcesses.FindIndex(p => p.Id == process.Id);
        if (existingIndex >= 0)
            _userCreatedProcesses[existingIndex] = process;
        else
            _userCreatedProcesses.Add(process);

        _log.Info($"User created process: {process.Name}");
        RebuildProcessCache();
        SaveUserStorage();
    }

    public void UpdatePreset(DeploymentPreset preset)
    {
        var index = _userCreatedPresets.FindIndex(p => p.Id == preset.Id);
        if (index != -1)
        {
            _userCreatedPresets[index] = preset;
            _log.Info($"Updated custom package: {preset.Name}");
            RebuildPresetCache();
            SaveUserStorage();
        }
        else
        {
            // If it's a built-in preset being "edited", we treat it as a new custom preset with the same ID
            // or we could throw. For now, let's just add it if not found.
            _userCreatedPresets.Add(preset);
            RebuildPresetCache();
            SaveUserStorage();
        }
    }

    public void UpdateProcess(DeploymentProcess process)
    {
        var index = _userCreatedProcesses.FindIndex(p => p.Id == process.Id);
        if (index != -1)
        {
            _userCreatedProcesses[index] = process;
            _log.Info($"Updated custom process: {process.Name}");
            RebuildProcessCache();
            SaveUserStorage();
        }
        else
        {
            // If it's a built-in process being "edited", we treat it as a new custom process
            _userCreatedProcesses.Add(process);
            _log.Info($"Promoted built-in process to custom: {process.Name}");
            RebuildProcessCache();
            SaveUserStorage();
        }
    }

    public bool DeletePreset(string presetId)
    {
        var index = _userCreatedPresets.FindIndex(p => p.Id == presetId);
        if (index < 0) return false;

        var removed = _userCreatedPresets[index];
        _userCreatedPresets.RemoveAt(index);
        _log.Info($"Deleted custom package: {removed.Name}");
        RebuildPresetCache();
        SaveUserStorage();
        return true;
    }

    public bool DeleteProcess(string processId)
    {
        var index = _userCreatedProcesses.FindIndex(p => p.Id == processId);
        if (index < 0) return false;

        var removed = _userCreatedProcesses[index];
        _userCreatedProcesses.RemoveAt(index);

        var existsInBase = _baseProcesses.Any(p => p.Id == processId);
        if (!existsInBase)
        {
            foreach (var preset in _userCreatedPresets)
            {
                if (preset.Steps.Count == 0) continue;
                var before = preset.Steps.Count;
                preset.Steps = preset.Steps.Where(s => s.ProcessId != processId).OrderBy(s => s.Order).ToList();
                if (preset.Steps.Count != before)
                {
                    for (var i = 0; i < preset.Steps.Count; i++)
                        preset.Steps[i].Order = (i + 1) * 10;
                }
            }
        }

        _log.Info($"Deleted custom process: {removed.Name}");
        RebuildProcessCache();
        SaveUserStorage();
        return true;
    }

    public IReadOnlyList<DeploymentProcess> GetAllAvailableProcesses()
    {
        return _cachedProcesses;
    }

    public IReadOnlyList<DeploymentPreset> GetAllPresets()
    {
        return _cachedPresets;
    }

    private void RebuildProcessCache()
    {
        var merged = new List<DeploymentProcess>(_baseProcesses);
        foreach (var custom in _userCreatedProcesses)
        {
            var idx = merged.FindIndex(p => p.Id == custom.Id);
            if (idx >= 0) merged[idx] = custom;
            else merged.Add(custom);
        }

        NormalizeProcessIcons(merged);
        NormalizeInstallerProcesses(merged);
        _cachedProcesses = merged;
    }

    private void RebuildPresetCache()
    {
        _cachedPresets = _userCreatedPresets.ToList();
    }

    private static bool NormalizeProcessIcons(List<DeploymentProcess> processes)
    {
        var changed = false;
        foreach (var process in processes)
        {
            if (process.NormalizeIconRecursively())
                changed = true;
        }

        return changed;
    }

    private bool NormalizeInstallerProcesses(List<DeploymentProcess> processes)
    {
        var changed = false;
        var normalization = _config.Config.InstallerService.Normalization;

        foreach (var p in processes)
        {
            if (p.Kind != ProcessKind.Installer) continue;

            var originalDownloadUrl = p.DownloadUrl;
            var originalDownloadBase = p.DownloadBaseFolderUrl;

            p.DownloadUrl = ApplyKnownUrlFixups(NormalizeAbsoluteUrl(p.DownloadUrl));
            p.DownloadBaseFolderUrl = NormalizeAbsoluteUrl(p.DownloadBaseFolderUrl);

            if (NormalizeKnownInstallerFixups(p, normalization))
                changed = true;

            if (!string.Equals(originalDownloadUrl, p.DownloadUrl, StringComparison.Ordinal) ||
                !string.Equals(originalDownloadBase, p.DownloadBaseFolderUrl, StringComparison.Ordinal))
            {
                changed = true;
            }

            var inferred =
                !string.IsNullOrWhiteSpace(p.DownloadBaseFolderUrl) ? InstallerSourceMode.DynamicWeb :
                !string.IsNullOrWhiteSpace(p.DownloadUrl) ? InstallerSourceMode.StaticWeb :
                InstallerSourceMode.StaticLocal;

            if (p.InstallerSourceMode == default && inferred != InstallerSourceMode.StaticLocal)
                p.InstallerSourceMode = inferred;

            if (p.InstallerSourceMode == InstallerSourceMode.StaticLocal)
            {
                p.DownloadUrl ??= string.Empty;
                p.DownloadBaseFolderUrl ??= string.Empty;
                p.DownloadSelectedFileName ??= string.Empty;
                p.DownloadSelectedFileTemplate ??= string.Empty;
                p.DownloadVersionFolderName ??= string.Empty;
            }

            if (p.InstallerSourceMode != InstallerSourceMode.DynamicWeb)
            {
                p.DownloadBaseFolderUrl ??= string.Empty;
                p.DownloadSelectedFileName ??= string.Empty;
                p.DownloadSelectedFileTemplate ??= string.Empty;
                p.DownloadVersionFolderName ??= string.Empty;
                p.DownloadUseLatestVersion = true;
            }

            if (p.InstallerSourceMode is InstallerSourceMode.StaticWeb or InstallerSourceMode.DynamicWeb)
            {
                var legacy = Path.Combine("Data", "installers", p.Id, "installer.exe");
                if (string.Equals((p.RelativePath ?? string.Empty).Trim(), legacy, StringComparison.OrdinalIgnoreCase))
                {
                    var desiredName = p.InstallerSourceMode == InstallerSourceMode.DynamicWeb
                        ? (string.IsNullOrWhiteSpace(p.DownloadSelectedFileName) ? p.DownloadSelectedFileTemplate : p.DownloadSelectedFileName)
                        : TryGetFileNameFromUrl(p.DownloadUrl);

                    desiredName = SanitizeFileName(desiredName);
                    if (!string.IsNullOrWhiteSpace(desiredName))
                        p.RelativePath = Path.Combine("Data", "installers", p.Id, desiredName);
                }
            }

            if (NormalizeKnownScriptFixups(p))
                changed = true;
        }

        return changed;
    }

    private static bool NormalizeKnownInstallerFixups(DeploymentProcess process, InstallerNormalizationConfig normalization)
    {
        var changed = false;

        if (NormalizeKnownRetailFixups(process, normalization))
            changed = true;

        if (IsSqlPassProcess(process))
        {
            var desiredUrl = ApplyKnownUrlFixups(NormalizeAbsoluteUrl(normalization.SqlPassFullInstallerUrl));
            if (!string.IsNullOrWhiteSpace(desiredUrl) &&
                (string.IsNullOrWhiteSpace(process.DownloadUrl) ||
                 string.Equals(Path.GetFileName(process.DownloadUrl), "SQLEXPR_x64_ENU.exe", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFileName(process.DownloadUrl), "SQL2022-SSEI-Expr.exe", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.Equals(process.DownloadUrl, desiredUrl, StringComparison.Ordinal))
                {
                    process.DownloadUrl = desiredUrl;
                    changed = true;
                }
            }

            var desiredFileName = SanitizeFileName(normalization.SqlPassFullInstallerFileName);
            if (!string.IsNullOrWhiteSpace(desiredFileName))
            {
                var normalizedRelative = (process.RelativePath ?? string.Empty).Replace('/', '\\');
                var legacyPrefix = Path.Combine("Data", "installers", process.Id).Replace('/', '\\') + "\\";
                if (string.IsNullOrWhiteSpace(normalizedRelative) ||
                    normalizedRelative.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var desiredRelativePath = Path.Combine("Data", "installers", process.Id, desiredFileName);
                    if (!string.Equals(process.RelativePath, desiredRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        process.RelativePath = desiredRelativePath;
                        changed = true;
                    }
                }
            }
        }

        if (NormalizeKnownSqlPassScriptFixups(process, normalization))
            changed = true;

        return changed;
    }

    private static bool NormalizeKnownRetailFixups(DeploymentProcess process, InstallerNormalizationConfig normalization)
    {
        if (!IsRetailProcess(process))
            return false;

        var changed = false;

        var desiredArgs = (normalization.RetailDefaultArgs ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(desiredArgs) &&
            string.IsNullOrWhiteSpace((process.Arguments ?? string.Empty).Trim()))
        {
            process.Arguments = desiredArgs;
            changed = true;
        }

        var desiredInputs = BuildRetailArgumentInputs(normalization);
        if (desiredInputs.Count > 0)
        {
            if (MergeRetailArgumentInputs(process, desiredInputs))
                changed = true;
        }

        if (LooksLikeLegacyRetailWrapper(process))
        {
            process.SubProcesses = new List<DeploymentSubProcess>();
            changed = true;
        }
        else if (RemoveRetailNormalizationSubProcess(process))
        {
            changed = true;
        }

        return changed;
    }

    private static bool LooksLikeLegacyRetailWrapper(DeploymentProcess process)
    {
        if (process.SubProcesses is not { Count: > 0 })
            return false;

        if (process.SubProcesses.Count == 1)
        {
            var only = process.SubProcesses[0].Process;
            return IsRetailInstallSubProcess(only) && RetailInstallMatchesParent(process, only!);
        }

        if (process.SubProcesses.Count == 2)
        {
            var first = process.SubProcesses[0].Process;
            var second = process.SubProcesses[1].Process;
            return IsRetailInstallSubProcess(first) &&
                   RetailInstallMatchesParent(process, first!) &&
                   IsRetailNormalizationSubProcess(second);
        }

        return false;
    }

    private static bool RemoveRetailNormalizationSubProcess(DeploymentProcess process)
    {
        if (process.SubProcesses is not { Count: > 0 })
            return false;

        var originalCount = process.SubProcesses.Count;
        process.SubProcesses = process.SubProcesses
            .Where(sp => !IsRetailNormalizationSubProcess(sp.Process))
            .ToList();

        return process.SubProcesses.Count != originalCount;
    }

    private static bool IsRetailInstallSubProcess(DeploymentProcess? process) =>
        process is not null &&
        process.Kind == ProcessKind.Installer &&
        string.Equals(Path.GetFileName(process.RelativePath ?? string.Empty), "RetailServer.exe", StringComparison.OrdinalIgnoreCase);

    private static bool RetailInstallMatchesParent(DeploymentProcess parent, DeploymentProcess child)
    {
        var parentArgs = (parent.Arguments ?? string.Empty).Trim();
        var childArgs = (child.Arguments ?? string.Empty).Trim();
        var argumentsMatch =
            string.Equals(parentArgs, childArgs, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(childArgs);

        return string.Equals((parent.RelativePath ?? string.Empty).Trim(), (child.RelativePath ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
               argumentsMatch &&
               parent.InstallerSourceMode == child.InstallerSourceMode &&
               string.Equals((parent.DownloadBaseFolderUrl ?? string.Empty).Trim(), (child.DownloadBaseFolderUrl ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
               string.Equals((parent.DownloadSelectedFileTemplate ?? string.Empty).Trim(), (child.DownloadSelectedFileTemplate ?? string.Empty).Trim(), StringComparison.Ordinal) &&
               parent.DownloadPickLatestFolderByName == child.DownloadPickLatestFolderByName &&
               parent.DownloadUseLatestVersion == child.DownloadUseLatestVersion &&
               string.Equals((parent.PortalId ?? string.Empty).Trim(), (child.PortalId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetailNormalizationSubProcess(DeploymentProcess? process)
    {
        if (process is null)
            return false;

        var name = process.Name ?? string.Empty;
        var script = process.ScriptContent ?? string.Empty;
        return process.Kind == ProcessKind.PowerShellScript &&
               (name.Contains("Normalizza configurazione SQL", StringComparison.OrdinalIgnoreCase) ||
                script.Contains("Retail: normalizzato endpoint DB", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRetailProcess(DeploymentProcess process) =>
        string.Equals(process.Id, "retail-server", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFileName(process.RelativePath ?? string.Empty), "RetailServer.exe", StringComparison.OrdinalIgnoreCase) ||
        process.Name.Contains("Passepartout Retail", StringComparison.OrdinalIgnoreCase);

    private static List<ArgumentInputDefinition> BuildRetailArgumentInputs(InstallerNormalizationConfig normalization)
    {
        var sqlServerDefault = (normalization.DefaultRetailSqlServer ?? string.Empty).Trim();
        var sqlUserDefaultRaw = (normalization.DefaultRetailSqlUser ?? string.Empty).Trim();
        var sqlUserDefault = string.IsNullOrWhiteSpace(sqlUserDefaultRaw) ? "sa" : sqlUserDefaultRaw;
        var dbNameDefault = (normalization.DefaultRetailDbName ?? string.Empty).Trim();
        var sqlPasswordDefault = (normalization.DefaultSqlPassSaPassword ?? string.Empty).Trim();

        return
        [
            new ArgumentInputDefinition
            {
                Key = "KLEVADEPLOY_RETAIL_SQL_SERVER",
                Label = "Server SQL",
                Description = "Nome istanza SQL (es. NOMEPC\\SQLPASS).",
                DefaultValue = sqlServerDefault,
                IsSecret = false,
                IsRequired = true
            },
            new ArgumentInputDefinition
            {
                Key = "KLEVADEPLOY_RETAIL_SQL_USER",
                Label = "Utente SQL",
                Description = "Utente SQL usato da Retail per connettersi al DB.",
                DefaultValue = sqlUserDefault,
                IsSecret = false,
                IsRequired = true
            },
            new ArgumentInputDefinition
            {
                Key = "KLEVADEPLOY_RETAIL_DB_NAME",
                Label = "Nome database",
                Description = "Nome database Retail.",
                DefaultValue = dbNameDefault,
                IsSecret = false,
                IsRequired = true
            },
            new ArgumentInputDefinition
            {
                Key = "KLEVADEPLOY_SQLPASS_SA_PASSWORD",
                Label = "Password SQL",
                Description = "Password dell'utente SQL usato da Retail per connettersi al DB.",
                DefaultValue = sqlPasswordDefault,
                IsSecret = true,
                IsRequired = true
            }
        ];
    }

    private static bool MergeRetailArgumentInputs(DeploymentProcess process, IReadOnlyList<ArgumentInputDefinition> desiredInputs)
    {
        var changed = false;
        var existing = process.ArgumentInputs ?? new List<ArgumentInputDefinition>();
        var existingByKey = existing.ToDictionary(x => (x.Key ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase);
        var merged = new List<ArgumentInputDefinition>();

        foreach (var desired in desiredInputs)
        {
            if (!existingByKey.TryGetValue(desired.Key, out var current))
            {
                merged.Add(CloneArgumentInput(desired));
                changed = true;
                continue;
            }

            var updated = CloneArgumentInput(current);
            if (string.IsNullOrWhiteSpace((updated.Label ?? string.Empty).Trim()))
            {
                updated.Label = desired.Label;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace((updated.Description ?? string.Empty).Trim()))
            {
                updated.Description = desired.Description;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace((updated.DefaultValue ?? string.Empty).Trim()) &&
                !string.IsNullOrWhiteSpace((desired.DefaultValue ?? string.Empty).Trim()))
            {
                updated.DefaultValue = desired.DefaultValue ?? string.Empty;
                changed = true;
            }

            if (updated.IsSecret != desired.IsSecret)
            {
                updated.IsSecret = desired.IsSecret;
                changed = true;
            }

            if (updated.IsRequired != desired.IsRequired)
            {
                updated.IsRequired = desired.IsRequired;
                changed = true;
            }

            merged.Add(updated);
        }

        foreach (var current in existing)
        {
            var key = (current.Key ?? string.Empty).Trim();
            if (desiredInputs.Any(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)))
                continue;

            merged.Add(CloneArgumentInput(current));
        }

        if (changed)
            process.ArgumentInputs = merged;

        return changed;
    }

    private static ArgumentInputDefinition CloneArgumentInput(ArgumentInputDefinition input) =>
        new()
        {
            Key = input.Key ?? string.Empty,
            Label = input.Label ?? string.Empty,
            Description = input.Description ?? string.Empty,
            DefaultValue = input.DefaultValue ?? string.Empty,
            IsSecret = input.IsSecret,
            IsRequired = input.IsRequired
        };

    private static bool NormalizeKnownSqlPassScriptFixups(DeploymentProcess process, InstallerNormalizationConfig normalization)
    {
        var changed = false;
        var desiredBootstrapper = SanitizeFileName(normalization.SqlPassFullInstallerFileName);

        if (!string.IsNullOrWhiteSpace(process.ScriptContent))
        {
            var original = process.ScriptContent;
            var updated = original;

            if (!string.IsNullOrWhiteSpace(desiredBootstrapper))
            {
                updated = updated.Replace(
                    "installers\\sql-express-2022-sqlpass\\SQLEXPR_x64_ENU.exe",
                    $"installers\\sql-express-2022-sqlpass\\{desiredBootstrapper}",
                    StringComparison.Ordinal);
            }

            const string oldExtractBlock = "$p = Start-Process -FilePath $installer -ArgumentList @('/q', ('/x:' + $extract)) -Wait -PassThru -WindowStyle Hidden\nif ($p.ExitCode -ne 0) { exit $p.ExitCode }\n\nif (!(Test-Path $setupInExtract)) { throw ('setup.exe non trovato in: ' + $extract) }\nexit 0\n";
            const string newExtractBlock = "$media = Join-Path $temp 'KlevaDeploy\\sql2022_media'\nNew-Item -ItemType Directory -Force -Path $media | Out-Null\n\n$downloadedCore = Join-Path $media 'SQLEXPR_x64_ENU.exe'\nif (!(Test-Path $downloadedCore)) {\n  $download = Start-Process -FilePath $installer -ArgumentList @('/Action=Download', ('/MediaPath=\"' + $media + '\"'), '/MediaType=Core', '/Quiet') -Wait -PassThru -WindowStyle Hidden\n  if ($download.ExitCode -ne 0) { exit $download.ExitCode }\n}\n\nif (!(Test-Path $downloadedCore)) { throw ('Media SQL Express non trovata in: ' + $media) }\n\n$p = Start-Process -FilePath $downloadedCore -ArgumentList @('/q', ('/x:' + $extract)) -Wait -PassThru -WindowStyle Hidden\nif ($p.ExitCode -ne 0) { exit $p.ExitCode }\n\nif (!(Test-Path $setupInExtract)) { throw ('setup.exe non trovato in: ' + $extract) }\nexit 0\n";

            if (updated.Contains(oldExtractBlock, StringComparison.Ordinal))
                updated = updated.Replace(oldExtractBlock, newExtractBlock, StringComparison.Ordinal);

            if (!string.Equals(updated, original, StringComparison.Ordinal))
            {
                process.ScriptContent = updated;
                changed = true;
            }
        }

        if (process.SubProcesses is null || process.SubProcesses.Count == 0)
            return changed;

        foreach (var subProcess in process.SubProcesses)
        {
            if (subProcess.Process is null)
                continue;

            if (NormalizeKnownSqlPassScriptFixups(subProcess.Process, normalization))
                changed = true;
        }

        return changed;
    }

    private static bool IsSqlPassProcess(DeploymentProcess process) =>
        string.Equals(process.Id, "sql-express-2022-sqlpass", StringComparison.OrdinalIgnoreCase) ||
        process.Name.Contains("SQLPASS", StringComparison.OrdinalIgnoreCase);

    private static bool NormalizeKnownScriptFixups(DeploymentProcess process)
    {
        var changed = false;

        if (!string.IsNullOrWhiteSpace(process.ScriptContent))
        {
            var fixedScript = process.ScriptContent.Replace(
                "$saPwdthrowthrow 'KLEVADEPLOY_SQLPASS_SA_PASSWORD non impostata.'",
                "throw 'KLEVADEPLOY_SQLPASS_SA_PASSWORD non impostata.'",
                StringComparison.Ordinal);

            if (!string.Equals(fixedScript, process.ScriptContent, StringComparison.Ordinal))
            {
                process.ScriptContent = fixedScript;
                changed = true;
            }
        }

        if (process.SubProcesses is null || process.SubProcesses.Count == 0)
            return changed;

        foreach (var subProcess in process.SubProcesses)
        {
            if (subProcess.Process is null)
                continue;

            if (NormalizeKnownScriptFixups(subProcess.Process))
                changed = true;
        }

        return changed;
    }

    private static string NormalizeAbsoluteUrl(string? value)
    {
        var raw = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        if (Uri.TryCreate(raw, UriKind.Absolute, out _)) return raw;
        if (!raw.Contains("://", StringComparison.Ordinal) &&
            Uri.TryCreate("https://" + raw, UriKind.Absolute, out _))
        {
            return "https://" + raw;
        }

        return raw;
    }

    private static string ApplyKnownUrlFixups(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        if (string.Equals(
                url,
                "https://download.microsoft.com/download/5/1/4/-4d30-4b85-b0d1-39533663a2f1/SQL2022-SSEI-Expr.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return "https://download.microsoft.com/download/5/1/4/5145fe04-4d30-4b85-b0d1-39533663a2f1/SQL2022-SSEI-Expr.exe";
        }

        return url;
    }

    private static string TryGetDataRelativePath(string? path)
    {
        var rel = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rel)) return string.Empty;
        if (Path.IsPathRooted(rel)) return string.Empty;

        var normalized = rel.Replace('/', '\\').TrimStart('\\');
        if (!normalized.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return normalized["Data\\".Length..];
    }

    private static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        name = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim();
    }

    private static string TryGetFileNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return string.Empty;
        return Path.GetFileName(uri.AbsolutePath) ?? string.Empty;
    }
}
