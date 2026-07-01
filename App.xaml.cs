using System.Net;
using System.Net.Http;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;
using KlevaDeploy.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace KlevaDeploy;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        InstallCrashHandlers();
        EnsureStandaloneStorageEnvironment();
        EnsureStandaloneBundledFiles();
        ApplyConfigEnvironmentDefaults();

        try
        {
            if (await TryHandleMsiWorkerModeAsync(e))
                return;

            if (await TryHandleUpdateModeAsync(e))
                return;

            if (!IsRunningAsAdmin() && !(e.Args ?? Array.Empty<string>()).Contains("--no-admin", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exePath))
                    {
                        var args = (e.Args ?? Array.Empty<string>()).Concat(new[] { "--no-admin" }).Select(QuoteArgument);
                        var psi = new ProcessStartInfo
                        {
                            FileName = exePath,
                            Arguments = string.Join(" ", args),
                            UseShellExecute = true,
                            Verb = "runas",
                        };

                        Process.Start(psi);
                        Shutdown(0);
                        return;
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    MessageBox.Show("Administrator privileges are required to run this installer silently. Operation was cancelled.", "KlevaDeploy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Shutdown(1);
                    return;
                }
                catch (Exception ex)
                {
                    WriteCrashLog("Elevation", ex);
                    MessageBox.Show(ex.ToString(), "KlevaDeploy failed to elevate", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown(1);
                    return;
                }
            }

            base.OnStartup(e);

            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();

            // Apply the theme from preferences via the service
            var themeService = _serviceProvider.GetRequiredService<IThemeService>();
            themeService.SetTheme(themeService.CurrentTheme);

            var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();
            if (await TryHandleHeadlessRunModeAsync(e, mainVm))
                return;

            var mainWindow = new MainWindow(mainVm);
            mainWindow.Show();

            await mainVm.InitializeCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
            MessageBox.Show(ex.ToString(), "KlevaDeploy crashed during startup", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void ApplyConfigEnvironmentDefaults()
    {
        try
        {
            var cfg = new AppConfigService().Config;
            var secureKey = (cfg.Msi.SecureCustomPropertiesKey ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(secureKey))
            {
                Environment.SetEnvironmentVariable("KLEVADEPLOY_MSI_SECURECUSTOMPROPERTIES_KEY", secureKey, EnvironmentVariableTarget.Process);
            }
        }
        catch
        {
        }
    }

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureStandaloneStorageEnvironment()
    {
        try
        {
            var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
            if (string.IsNullOrWhiteSpace(storageDir))
                storageDir = Path.Combine(AppContext.BaseDirectory, "Data");

            var tempDir = Path.Combine(storageDir, "temp");
            Directory.CreateDirectory(storageDir);
            Directory.CreateDirectory(tempDir);

            Environment.SetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR", storageDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("KLEVADEPLOY_DATA_DIR", storageDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("KLEVADEPLOY_TEMP_DIR", tempDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("TEMP", tempDir, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("TMP", tempDir, EnvironmentVariableTarget.Process);
        }
        catch { }
    }

    private static void EnsureStandaloneBundledFiles()
    {
        try
        {
            var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
            if (string.IsNullOrWhiteSpace(storageDir))
                storageDir = Path.Combine(AppContext.BaseDirectory, "Data");

            Directory.CreateDirectory(storageDir);

            var asm = Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            if (names is null || names.Length == 0) return;

            const string defaultsPrefix = "KlevaDeploy.Defaults.";
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                string? relativeOut = null;
                if (string.Equals(name, "KlevaDeploy.appsettings.json", StringComparison.OrdinalIgnoreCase))
                {
                    relativeOut = "appsettings.json";
                }
                else if (name.StartsWith(defaultsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var fileName = name[defaultsPrefix.Length..];
                    if (string.IsNullOrWhiteSpace(fileName)) continue;
                    relativeOut = Path.Combine("Defaults", fileName);
                }
                else
                {
                    continue;
                }

                var outPath = Path.Combine(storageDir, relativeOut);
                if (File.Exists(outPath)) continue;

                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                using var stream = asm.GetManifestResourceStream(name);
                if (stream is null) continue;

                using var fs = File.Create(outPath);
                stream.CopyTo(fs);
            }
        }
        catch
        {
        }
    }

    private static string QuoteArgument(string arg)
    {
        var a = arg ?? string.Empty;
        if (a.Length == 0) return "\"\"";
        if (a.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0) return a;
        return "\"" + a.Replace("\"", "\\\"") + "\"";
    }

    private static async Task<bool> TryHandleMsiWorkerModeAsync(StartupEventArgs e)
    {
        var args = e.Args ?? Array.Empty<string>();
        if (!args.Contains("--msi-worker", StringComparer.OrdinalIgnoreCase))
            return false;

        var pipeName = TryGetArgString(args, "--pipe");
        var msiPath = TryGetArgString(args, "--msi");
        var msiArgs = TryGetArgString(args, "--msi-args") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(msiPath))
        {
            Current?.Shutdown(-1);
            return true;
        }

        int exitCode;
        try
        {
            exitCode = await RunMsiWorkerAsync(msiPath, msiArgs, pipeName);
        }
        catch
        {
            exitCode = -1;
        }

        Current?.Shutdown(exitCode);
        return true;
    }

    private static async Task<bool> TryHandleUpdateModeAsync(StartupEventArgs e)
    {
        var args = e.Args ?? Array.Empty<string>();
        if (!args.Contains("--apply-update", StringComparer.OrdinalIgnoreCase))
            return false;

        var pid = TryGetArgInt(args, "--pid");
        var target = TryGetArgString(args, "--target");
        var source = Environment.ProcessPath;

        if (pid is null || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(source))
        {
            Current?.Shutdown(-1);
            return true;
        }

        try
        {
            try
            {
                var p = Process.GetProcessById(pid.Value);
                await Task.Run(() => p.WaitForExit(30_000));
            }
            catch { }

            Exception? lastCopyError = null;
            var copied = false;
            for (var i = 0; i < 30; i++)
            {
                try
                {
                    File.Copy(source, target, overwrite: true);
                    copied = true;
                    break;
                }
                catch (Exception ex)
                {
                    lastCopyError = ex;
                    await Task.Delay(200);
                }
            }

            if (!copied)
            {
                MessageBox.Show(
                    $"Unable to apply the downloaded update to:\n{target}\n\n{lastCopyError?.Message ?? "Unknown copy error."}",
                    "KlevaDeploy update failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Current?.Shutdown(-1);
                return true;
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        finally
        {
            Current?.Shutdown();
        }

        return true;
    }

    private static async Task<bool> TryHandleHeadlessRunModeAsync(StartupEventArgs e, MainViewModel mainVm)
    {
        var args = e.Args ?? Array.Empty<string>();
        var processId = TryGetArgString(args, "--run-process");
        if (string.IsNullOrWhiteSpace(processId))
            return false;

        await mainVm.InitializeCommand.ExecuteAsync(null);
        var exit = await mainVm.RunSingleProcessHeadlessAsync(processId);
        Current?.Shutdown(exit);
        return true;
    }

    private static async Task<int> RunMsiWorkerAsync(string msiPath, string msiArgs, string pipeName)
    {
        await using var pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.Out,
            options: PipeOptions.Asynchronous);

        for (var attempt = 0; attempt < 200; attempt++)
        {
            try
            {
                await pipe.ConnectAsync(50);
                break;
            }
            catch
            {
                await Task.Delay(50);
            }
        }

        await using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };

        var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        var msiLogDir = Path.Combine(dataDir, "msi-logs");
        Directory.CreateDirectory(msiLogDir);

        var logPath = Path.Combine(msiLogDir, $"msi_{DateTimeOffset.Now:yyyyMMdd_HHmmss}_{Environment.ProcessId}.log");

        var fullArgs = BuildMsiInstallCommandLine(msiArgs);

        var state = new MsiProgressState();
        var callbackState = new MsiCallbackState(writer, state);
        var gcHandle = GCHandle.Alloc(callbackState);

        var logMode =
            NativeMsi.INSTALLLOGMODE_PROGRESS |
            NativeMsi.INSTALLLOGMODE_FATALEXIT |
            NativeMsi.INSTALLLOGMODE_ERROR |
            NativeMsi.INSTALLLOGMODE_WARNING |
            NativeMsi.INSTALLLOGMODE_USER |
            NativeMsi.INSTALLLOGMODE_INFO |
            NativeMsi.INSTALLLOGMODE_ACTIONSTART |
            NativeMsi.INSTALLLOGMODE_ACTIONDATA |
            NativeMsi.INSTALLLOGMODE_COMMONDATA |
            NativeMsi.INSTALLLOGMODE_INITIALIZE |
            NativeMsi.INSTALLLOGMODE_TERMINATE |
            NativeMsi.INSTALLLOGMODE_SHOWDIALOG |
            NativeMsi.INSTALLLOGMODE_RESOLVESOURCE |
            NativeMsi.INSTALLLOGMODE_OUTOFDISKSPACE |
            NativeMsi.INSTALLLOGMODE_FILESINUSE |
            NativeMsi.INSTALLLOGMODE_RMFILESINUSE;

        var previousInternalUi = NativeMsi.MsiSetInternalUI(NativeMsi.INSTALLUILEVEL_NONE, IntPtr.Zero);
        NativeMsi.InstallUIHandlerRecord handler = MsiExternalUiHandlerRecord;
        var previousExternalUi = NativeMsi.MsiSetExternalUIRecord(
            handler,
            NativeMsi.INSTALLLOGMODE_PROGRESS |
            NativeMsi.INSTALLLOGMODE_FATALEXIT |
            NativeMsi.INSTALLLOGMODE_ERROR |
            NativeMsi.INSTALLLOGMODE_WARNING |
            NativeMsi.INSTALLLOGMODE_USER |
            NativeMsi.INSTALLLOGMODE_INFO |
            NativeMsi.INSTALLLOGMODE_ACTIONSTART |
            NativeMsi.INSTALLLOGMODE_ACTIONDATA |
            NativeMsi.INSTALLLOGMODE_COMMONDATA |
            NativeMsi.INSTALLLOGMODE_INITIALIZE |
            NativeMsi.INSTALLLOGMODE_TERMINATE |
            NativeMsi.INSTALLLOGMODE_SHOWDIALOG |
            NativeMsi.INSTALLLOGMODE_RESOLVESOURCE |
            NativeMsi.INSTALLLOGMODE_OUTOFDISKSPACE |
            NativeMsi.INSTALLLOGMODE_FILESINUSE |
            NativeMsi.INSTALLLOGMODE_RMFILESINUSE,
            GCHandle.ToIntPtr(gcHandle));

        WriteJsonLine(writer, new MsiWorkerMessage
        {
            Type = "start",
            Message = $"Starting MSI install: {msiPath}",
            LogPath = logPath
        });

        try
        {
            callbackState.LogPath = logPath;
            _ = NativeMsi.MsiEnableLog(logMode, logPath, 0);
            var rc = NativeMsi.MsiInstallProduct(msiPath, fullArgs);
            var exit = unchecked((int)rc);
            WriteJsonLine(writer, new MsiWorkerMessage { Type = "done", ExitCode = exit, LogPath = logPath });
            return exit;
        }
        catch (Exception ex)
        {
            var code = -1;
            WriteJsonLine(writer, new MsiWorkerMessage
            {
                Type = "done",
                ExitCode = code,
                Message = $"{ex.GetType().Name}: {ex.Message}",
                LogPath = logPath
            });
            return code;
        }
        finally
        {
            NativeMsi.MsiSetExternalUIRecord(previousExternalUi, 0, IntPtr.Zero);
            NativeMsi.MsiSetInternalUI(previousInternalUi, IntPtr.Zero);
            if (gcHandle.IsAllocated) gcHandle.Free();
        }
    }

    private static string BuildMsiInstallCommandLine(string args)
    {
        var trimmed = (args ?? string.Empty).Trim();
        var safe = StripMsiUiSwitches(trimmed);
        safe = Environment.ExpandEnvironmentVariables(safe);
        safe = NormalizeKnownRetailAliases(safe);
        safe = NormalizeSqlServerInstanceEndpoints(safe);
        safe = EnsureSecureCustomProperties(safe);
        return safe.Trim();
    }

    private static string NormalizeSqlServerInstanceEndpoints(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return string.Empty;

        static bool TryGetKeyValue(string token, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            var t = (token ?? string.Empty).Trim();
            if (t.Length == 0) return false;
            if (t[0] == '/' || t[0] == '-') return false;

            var eq = t.IndexOf('=');
            if (eq <= 0 || eq >= t.Length - 1) return false;
            key = t[..eq].Trim();
            value = t[(eq + 1)..].Trim().Trim('"');
            return key.Length > 0;
        }

        var tokens = TokenizeCommandLine(args);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (!TryGetKeyValue(tokens[i], out var key, out var value)) continue;

            if (!string.Equals(key, "IPSERVERDATABASE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, "IpServerDatabase", StringComparison.OrdinalIgnoreCase))
                continue;

            var resolved = TryResolveSqlServerTcpEndpoint(value);
            if (string.IsNullOrWhiteSpace(resolved)) continue;
            tokens[i] = $"{key}={resolved}";
        }

        return string.Join(' ', tokens).Trim();
    }

    private static string? TryResolveSqlServerTcpEndpoint(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        if (!trimmed.Contains('\\')) return null;
        if (trimmed.Contains(',')) return trimmed;

        var parts = trimmed.Split(new[] { '\\' }, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;

        var host = parts[0].Trim();
        var instance = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(instance)) return null;

        var port = TryReadSqlInstanceTcpPort(instance);
        if (string.IsNullOrWhiteSpace(port)) return null;

        return $"{host},{port}";
    }

    private static string? TryReadSqlInstanceTcpPort(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName)) return null;

        var normalized = instanceName.Trim();
        var candidates = new[]
        {
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL15.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL14.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL13.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL12.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll",
            $@"SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL11.{normalized}\MSSQLServer\SuperSocketNetLib\Tcp\IPAll"
        };

        foreach (var subKey in candidates)
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            if (key is null) continue;

            var tcpPort = key.GetValue("TcpPort") as string;
            if (!string.IsNullOrWhiteSpace(tcpPort)) return tcpPort.Trim();

            var dynamicPort = key.GetValue("TcpDynamicPorts") as string;
            if (!string.IsNullOrWhiteSpace(dynamicPort)) return dynamicPort.Trim();
        }

        return null;
    }

    private static string NormalizeKnownRetailAliases(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return string.Empty;

        static string? TryGetValue(IReadOnlyList<string> tokens, string key)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = (tokens[i] ?? string.Empty).Trim();
                if (!t.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) continue;
                return t[(key.Length + 1)..];
            }
            return null;
        }

        static bool HasExact(IReadOnlyList<string> tokens, string key)
        {
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = (tokens[i] ?? string.Empty).Trim();
                if (t.StartsWith($"{key}=", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        var tokens = TokenizeCommandLine(args);

        static void EnsureAlias(List<string> tokens, string fromKey, string toKey)
        {
            if (HasExact(tokens, toKey)) return;
            var v = TryGetValue(tokens, fromKey);
            if (string.IsNullOrWhiteSpace(v)) return;
            tokens.Add($"{toKey}={v}");
        }

        EnsureAlias(tokens, "INSTALLAZIONEAUTOMATICA", "InstallazioneAutomatica");
        EnsureAlias(tokens, "REINSTALLMODE", "ReinstallMode");
        EnsureAlias(tokens, "IPSERVERDATABASE", "IpServerDatabase");
        EnsureAlias(tokens, "PORTASERVER", "PortaServer");
        EnsureAlias(tokens, "NOMEDATABASE", "NomeDatabase");
        EnsureAlias(tokens, "PASSWORDDATABASE", "PasswordDatabase");
        EnsureAlias(tokens, "LOGFILE", "LogFile");

        EnsureAlias(tokens, "InstallazioneAutomatica", "INSTALLAZIONEAUTOMATICA");
        EnsureAlias(tokens, "ReinstallMode", "REINSTALLMODE");
        EnsureAlias(tokens, "IpServerDatabase", "IPSERVERDATABASE");
        EnsureAlias(tokens, "PortaServer", "PORTASERVER");
        EnsureAlias(tokens, "NomeDatabase", "NOMEDATABASE");
        EnsureAlias(tokens, "PasswordDatabase", "PASSWORDDATABASE");
        EnsureAlias(tokens, "LogFile", "LOGFILE");

        return string.Join(' ', tokens).Trim();
    }

    private static string EnsureSecureCustomProperties(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return string.Empty;

        var secureKey = (Environment.GetEnvironmentVariable("KLEVADEPLOY_MSI_SECURECUSTOMPROPERTIES_KEY") ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(secureKey)) return args;
        var tokens = TokenizeCommandLine(args);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingIdx = -1;
        var existingValue = string.Empty;

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = (tokens[i] ?? string.Empty).Trim();
            if (t.StartsWith($"{secureKey}=", StringComparison.OrdinalIgnoreCase))
            {
                existingIdx = i;
                var eq = t.IndexOf('=');
                existingValue = eq >= 0 ? t[(eq + 1)..].Trim().Trim('"') : string.Empty;
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(existingValue))
        {
            var existingParts = existingValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in existingParts)
            {
                if (!string.IsNullOrWhiteSpace(p))
                    names.Add(p.Trim().ToUpperInvariant());
            }
        }

        foreach (var t in tokens)
        {
            var token = (t ?? string.Empty).Trim();
            if (token.Length == 0) continue;
            if (token[0] == '/' || token[0] == '-') continue;

            var eq = token.IndexOf('=');
            if (eq <= 0) continue;

            var name = token[..eq].Trim();
            if (name.Length == 0) continue;
            if (!IsMsiPropertyName(name)) continue;
            if (string.Equals(name, secureKey, StringComparison.OrdinalIgnoreCase)) continue;

            names.Add(name.ToUpperInvariant());
        }

        if (names.Count == 0) return args.Trim();

        var mergedToken = $"{secureKey}={string.Join(';', names)}";
        if (existingIdx >= 0)
        {
            tokens[existingIdx] = mergedToken;
            return string.Join(' ', tokens).Trim();
        }

        return $"{args.Trim()} {mergedToken}".Trim();
    }

    private static bool IsMsiPropertyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        if (!IsNameChar(name[0]) || char.IsDigit(name[0])) return false;
        for (var i = 1; i < name.Length; i++)
        {
            if (!IsNameChar(name[i])) return false;
        }
        return true;
    }

    private static string StripMsiUiSwitches(string args)
    {
        if (string.IsNullOrWhiteSpace(args)) return string.Empty;

        var tokens = TokenizeCommandLine(args);
        var kept = new List<string>(tokens.Count);

        foreach (var t in tokens)
        {
            var normalized = t.Trim();
            if (normalized.Length == 0) continue;

            var n = normalized.TrimStart();
            if (n.StartsWith("/q", StringComparison.OrdinalIgnoreCase) ||
                n.StartsWith("-q", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "/quiet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "-quiet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "/passive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "-passive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "/qb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n, "/qn", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            kept.Add(normalized);
        }

        return string.Join(' ', kept);
    }

    private static List<string> TokenizeCommandLine(string commandLine)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(commandLine)) return tokens;

        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (c == '"')
            {
                inQuotes = !inQuotes;
                sb.Append(c);
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(c);
        }

        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static void WriteJsonLine(StreamWriter writer, MsiWorkerMessage message)
    {
        var json = JsonSerializer.Serialize(message);
        writer.WriteLine(json);
    }

    private static bool TryUpdateProgress(MsiProgressState state, IntPtr recordHandle, out int percent)
    {
        percent = 0;

        try
        {
            var cmd = NativeMsi.MsiRecordGetInteger(recordHandle, 1);
            var v2 = NativeMsi.MsiRecordGetInteger(recordHandle, 2);
            var v3 = NativeMsi.MsiRecordGetInteger(recordHandle, 3);

            switch (cmd)
            {
                case 0:
                    state.TotalTicks = Math.Max(0, v2);
                    state.CurrentTicks = 0;
                    state.Forward = v3 == 0;
                    break;
                case 1:
                    state.TicksPerActionData = Math.Max(0, v2);
                    state.AutoTickByActionData = v3 == 1;
                    break;
                case 2:
                    var inc = Math.Max(0, v2);
                    state.CurrentTicks += state.Forward ? inc : -inc;
                    if (state.CurrentTicks < 0) state.CurrentTicks = 0;
                    break;
                case 3:
                    state.TotalTicks += Math.Max(0, v2);
                    break;
                default:
                    return false;
            }

            if (state.TotalTicks <= 0) return false;

            var p = (int)Math.Round((double)state.CurrentTicks * 100.0 / state.TotalTicks);
            if (p < 0) p = 0;
            if (p > 100) p = 100;
            percent = p;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int MsiExternalUiHandlerRecord(IntPtr context, uint messageType, IntPtr recordHandle)
    {
        if (context == IntPtr.Zero) return NativeMsi.IDOK;

        var handle = GCHandle.FromIntPtr(context);
        if (handle.Target is not MsiCallbackState state) return NativeMsi.IDOK;

        var msgType = messageType & 0xFF000000;
        var message = recordHandle != IntPtr.Zero ? NativeMsi.FormatRecord(recordHandle) : string.Empty;

        if (msgType == NativeMsi.INSTALLMESSAGE_PROGRESS)
        {
            if (recordHandle != IntPtr.Zero &&
                TryUpdateProgress(state.Progress, recordHandle, out var percent) &&
                percent != state.LastPercent)
            {
                state.LastPercent = percent;
                WriteJsonLine(state.Writer, new MsiWorkerMessage
                {
                    Type = "progress",
                    Percent = percent,
                    CurrentTicks = state.Progress.CurrentTicks,
                    TotalTicks = state.Progress.TotalTicks
                });
            }

            return NativeMsi.IDOK;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_ACTIONSTART)
        {
            var actionName = recordHandle != IntPtr.Zero ? NativeMsi.GetRecordString(recordHandle, 1) : string.Empty;
            var actionDesc = recordHandle != IntPtr.Zero ? NativeMsi.GetRecordString(recordHandle, 2) : string.Empty;
            WriteJsonLine(state.Writer, new MsiWorkerMessage
            {
                Type = "actionStart",
                Message = string.IsNullOrWhiteSpace(actionDesc) ? actionName : $"{actionName} | {actionDesc}"
            });
            return NativeMsi.IDOK;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_ERROR || msgType == NativeMsi.INSTALLMESSAGE_FATALEXIT)
        {
            WriteJsonLine(state.Writer, new MsiWorkerMessage { Type = "error", Message = message });
            return NativeMsi.IDOK;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_WARNING)
        {
            WriteJsonLine(state.Writer, new MsiWorkerMessage { Type = "warning", Message = message });
            return NativeMsi.IDOK;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_USER ||
            msgType == NativeMsi.INSTALLMESSAGE_INFO ||
            msgType == NativeMsi.INSTALLMESSAGE_ACTIONDATA)
        {
            if (!string.IsNullOrWhiteSpace(message))
                WriteJsonLine(state.Writer, new MsiWorkerMessage { Type = "info", Message = message });
            return NativeMsi.IDOK;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_FILESINUSE ||
            msgType == NativeMsi.INSTALLMESSAGE_RMFILESINUSE)
        {
            WriteJsonLine(state.Writer, new MsiWorkerMessage { Type = "warning", Message = "MSI reported files in use. Continuing automatically." });
            return NativeMsi.IDIGNORE;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_SHOWDIALOG)
        {
            if (!string.IsNullOrWhiteSpace(message))
                WriteJsonLine(state.Writer, new MsiWorkerMessage { Type = "info", Message = message });
            return NativeMsi.IDOK;
        }

        if (msgType == NativeMsi.INSTALLMESSAGE_RESOLVESOURCE ||
            msgType == NativeMsi.INSTALLMESSAGE_OUTOFDISKSPACE)
        {
            WriteJsonLine(state.Writer, new MsiWorkerMessage
            {
                Type = "error",
                Message = $"MSI requested UI-only interaction (messageType=0x{msgType:X8}). Installation cancelled to avoid showing installer UI."
            });
            return NativeMsi.IDCANCEL;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            WriteJsonLine(state.Writer, new MsiWorkerMessage
            {
                Type = "debug",
                Message = $"MSI messageType=0x{msgType:X8}: {message}"
            });
        }

        return NativeMsi.IDOK;
    }

    private static int? TryGetArgInt(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], out var v) ? v : null;
        }
        return null;
    }

    private static string? TryGetArgString(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) continue;
            return args[i + 1];
        }
        return null;
    }

    private sealed class MsiProgressState
    {
        public int TotalTicks { get; set; }
        public int CurrentTicks { get; set; }
        public bool Forward { get; set; } = true;
        public int TicksPerActionData { get; set; }
        public bool AutoTickByActionData { get; set; }
    }

    private sealed class MsiWorkerMessage
    {
        public string Type { get; set; } = string.Empty;
        public string? Message { get; set; }
        public int? Percent { get; set; }
        public int? CurrentTicks { get; set; }
        public int? TotalTicks { get; set; }
        public int? ExitCode { get; set; }
        public string? LogPath { get; set; }
    }

    private sealed class MsiCallbackState
    {
        public StreamWriter Writer { get; }
        public MsiProgressState Progress { get; }
        public int LastPercent { get; set; } = -1;
        public string? LogPath { get; set; }

        public MsiCallbackState(StreamWriter writer, MsiProgressState progress)
        {
            Writer = writer;
            Progress = progress;
        }
    }

    private static class NativeMsi
    {
        public const int INSTALLUILEVEL_NOCHANGE = 0;
        public const int INSTALLUILEVEL_DEFAULT = 1;
        public const int INSTALLUILEVEL_NONE = 2;

        public const int IDOK = 1;
        public const int IDCANCEL = 2;
        public const int IDABORT = 3;
        public const int IDRETRY = 4;
        public const int IDIGNORE = 5;
        public const int IDYES = 6;
        public const int IDNO = 7;

        public const uint INSTALLMESSAGE_FATALEXIT = 0x00000000;
        public const uint INSTALLMESSAGE_ERROR = 0x01000000;
        public const uint INSTALLMESSAGE_WARNING = 0x02000000;
        public const uint INSTALLMESSAGE_USER = 0x03000000;
        public const uint INSTALLMESSAGE_INFO = 0x04000000;
        public const uint INSTALLMESSAGE_FILESINUSE = 0x05000000;
        public const uint INSTALLMESSAGE_RESOLVESOURCE = 0x06000000;
        public const uint INSTALLMESSAGE_OUTOFDISKSPACE = 0x07000000;
        public const uint INSTALLMESSAGE_ACTIONSTART = 0x08000000;
        public const uint INSTALLMESSAGE_ACTIONDATA = 0x09000000;
        public const uint INSTALLMESSAGE_PROGRESS = 0x0A000000;
        public const uint INSTALLMESSAGE_COMMONDATA = 0x0B000000;
        public const uint INSTALLMESSAGE_INITIALIZE = 0x0C000000;
        public const uint INSTALLMESSAGE_TERMINATE = 0x0D000000;
        public const uint INSTALLMESSAGE_SHOWDIALOG = 0x0E000000;
        public const uint INSTALLMESSAGE_RMFILESINUSE = 0x19000000;

        public const uint INSTALLLOGMODE_FATALEXIT = 0x00000001;
        public const uint INSTALLLOGMODE_ERROR = 0x00000002;
        public const uint INSTALLLOGMODE_WARNING = 0x00000004;
        public const uint INSTALLLOGMODE_USER = 0x00000008;
        public const uint INSTALLLOGMODE_INFO = 0x00000010;
        public const uint INSTALLLOGMODE_ACTIONSTART = 0x00000020;
        public const uint INSTALLLOGMODE_ACTIONDATA = 0x00000040;
        public const uint INSTALLLOGMODE_COMMONDATA = 0x00000080;
        public const uint INSTALLLOGMODE_PROGRESS = 0x00000400;
        public const uint INSTALLLOGMODE_INITIALIZE = 0x00000800;
        public const uint INSTALLLOGMODE_TERMINATE = 0x00001000;
        public const uint INSTALLLOGMODE_SHOWDIALOG = 0x00002000;
        public const uint INSTALLLOGMODE_RMFILESINUSE = 0x00020000;
        public const uint INSTALLLOGMODE_FILESINUSE = 0x00040000;
        public const uint INSTALLLOGMODE_RESOLVESOURCE = 0x00080000;
        public const uint INSTALLLOGMODE_OUTOFDISKSPACE = 0x00100000;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate int InstallUIHandlerRecord(IntPtr context, uint messageType, IntPtr recordHandle);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern int MsiSetInternalUI(int dwUILevel, IntPtr phWnd);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern InstallUIHandlerRecord? MsiSetExternalUIRecord(InstallUIHandlerRecord? puiHandler, uint dwMessageFilter, IntPtr pvContext);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern uint MsiInstallProduct(string szPackagePath, string szCommandLine);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern uint MsiEnableLog(uint dwLogMode, string szLogFile, uint dwLogAttributes);

        [DllImport("msi.dll")]
        public static extern int MsiRecordGetInteger(IntPtr hRecord, int iField);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern uint MsiRecordGetString(IntPtr hRecord, int iField, StringBuilder szValueBuf, ref uint pcchValueBuf);

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        public static extern uint MsiFormatRecord(IntPtr hInstall, IntPtr hRecord, StringBuilder szResultBuf, ref uint pcchResultBuf);

        public static string GetRecordString(IntPtr recordHandle, int field)
        {
            var cap = 256u;
            var sb = new StringBuilder((int)cap);
            var rc = MsiRecordGetString(recordHandle, field, sb, ref cap);
            if (rc == 0) return sb.ToString();

            sb = new StringBuilder((int)(cap + 1));
            rc = MsiRecordGetString(recordHandle, field, sb, ref cap);
            return rc == 0 ? sb.ToString() : string.Empty;
        }

        public static string FormatRecord(IntPtr recordHandle)
        {
            var cap = 512u;
            var sb = new StringBuilder((int)cap);
            var rc = MsiFormatRecord(IntPtr.Zero, recordHandle, sb, ref cap);
            if (rc == 0) return sb.ToString();

            sb = new StringBuilder((int)(cap + 1));
            rc = MsiFormatRecord(IntPtr.Zero, recordHandle, sb, ref cap);
            return rc == 0 ? sb.ToString() : string.Empty;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Shared cookie container — persists auth session across all HTTP calls
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false,
            UseCookies = true,
        };
        var httpClient = new HttpClient(handler);

        services.AddSingleton(cookieContainer);
        services.AddSingleton(handler);
        services.AddSingleton(httpClient);

        // Services
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IAppConfigService, AppConfigService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IPreferencesService, PreferencesService>();
        services.AddSingleton<IInstallerService, InstallerService>();
        services.AddSingleton<ILicenseScraperService, LicenseScraperService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IDownloadDirectoryListingService, DownloadDirectoryListingService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IPresetIconService, PresetIconService>();

        // ViewModels
        services.AddSingleton<LogViewModel>();
        // LoginViewModel is transient — a new instance per dialog
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<IInstallerService>(),
            sp.GetRequiredService<IUpdateService>(),
            sp.GetRequiredService<IAuthService>(),
            sp.GetRequiredService<IDownloadDirectoryListingService>(),
            sp.GetRequiredService<IAppUpdateService>(),
            sp.GetRequiredService<IProcessExecutionService>(),
            sp.GetRequiredService<ILicenseScraperService>(),
            sp.GetRequiredService<ILogService>(),
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IPresetIconService>(),
            sp.GetRequiredService<IPreferencesService>(),
            loginVmFactory: () => sp.GetRequiredService<LoginViewModel>(),
            sp.GetRequiredService<LogViewModel>()
        ));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void InstallCrashHandlers()
    {
        // NOTE: WPF WinExe apps often crash without a console stack trace.
        // These handlers persist a stack trace to Data\crash.log.
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            MessageBox.Show(args.Exception.ToString(), "Unhandled UI exception", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                WriteCrashLog("AppDomain.UnhandledException", ex);
            }
            else
            {
                WriteCrashLog("AppDomain.UnhandledException", new Exception($"Non-exception unhandled error: {args.ExceptionObject}"));
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static void WriteCrashLog(string context, Exception ex)
    {
        try
        {
            var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dataDir);
            var crashPath = Path.Combine(dataDir, "crash.log");

            File.AppendAllText(crashPath,
                $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 120)}{Environment.NewLine}");
        }
        catch
        {
            // Last resort: swallow. We don't want crash-logging to crash.
        }
    }
}
