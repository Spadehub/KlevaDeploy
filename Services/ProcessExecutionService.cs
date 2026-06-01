using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private readonly ILogService _log;

    private static readonly SemaphoreSlim UnrarInstallGate = new(1, 1);
    private static string? _cachedUnrarCliPath;
    private static readonly SemaphoreSlim SevenZipInstallGate = new(1, 1);
    private static string? _cached7ZipPath;

    public ProcessExecutionService(ILogService log) => _log = log;

    public async Task<string> Ensure7ZipInstalledAsync(CancellationToken ct = default)
    {
        await SevenZipInstallGate.WaitAsync(ct);
        try
        {
            var cached = _cached7ZipPath;
            if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                return cached;

            var toolsDir = GetWritableToolsDir();
            var installDir = Path.Combine(toolsDir, "7zip");
            Directory.CreateDirectory(installDir);

            var localCandidate = Path.Combine(installDir, "7z.exe");
            if (File.Exists(localCandidate))
            {
                _cached7ZipPath = localCandidate;
                return localCandidate;
            }

            var pfCandidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "7-Zip",
                "7z.exe");
            if (File.Exists(pfCandidate))
            {
                _cached7ZipPath = pfCandidate;
                return pfCandidate;
            }

            var msiUrl = "https://www.7-zip.org/a/7z2408-x64.msi";
            var msiPath = Path.Combine(GetTempDir(), $"KlevaDeploy_{Guid.NewGuid():N}_7z2408-x64.msi");

            try
            {
                _log.Info("Downloading 7-Zip...");
                await DownloadToFileAsync(new[] { msiUrl }, msiPath, ct);

                _log.Info("Installing 7-Zip...");
                var installArgs =
                    $"/i \"{msiPath}\" /qn /norestart " +
                    $"INSTALLDIR=\"{installDir}\" TARGETDIR=\"{installDir}\"";

                var result = await RunAsync("msiexec.exe", installArgs, runAsAdmin: false, ct);
                if (result.ExitCode != 0)
                {
                    result = await RunAsync("msiexec.exe", installArgs, runAsAdmin: true, ct);
                    if (result.ExitCode != 0)
                        throw new InvalidOperationException($"7-Zip MSI install failed (exit {result.ExitCode}).");
                }
            }
            finally
            {
                try { if (File.Exists(msiPath)) File.Delete(msiPath); } catch { }
            }

            if (File.Exists(localCandidate))
            {
                _cached7ZipPath = localCandidate;
                return localCandidate;
            }

            if (File.Exists(pfCandidate))
            {
                _cached7ZipPath = pfCandidate;
                return pfCandidate;
            }

            throw new InvalidOperationException("7-Zip installed but 7z.exe was not found.");
        }
        finally
        {
            SevenZipInstallGate.Release();
        }
    }

    public async Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info(BuildStartProcessMessage(executablePath, arguments, runAsAdmin));

        var storageDir = GetStorageDir();
        var tempDir = GetTempDir(storageDir);
        try { Directory.CreateDirectory(tempDir); } catch { }

        string? adminCapturePath = null;
        var captureAdminOutputToFile = runAsAdmin;

        if (captureAdminOutputToFile)
            adminCapturePath = Path.Combine(tempDir, $"KlevaDeploy_{Guid.NewGuid():N}.out.txt");

        var psi = new ProcessStartInfo
        {
            FileName = captureAdminOutputToFile ? "cmd.exe" : executablePath,
            Arguments = captureAdminOutputToFile
                ? BuildCmdCaptureArguments(executablePath, arguments, adminCapturePath!, storageDir, tempDir)
                : arguments,
            CreateNoWindow = true,
            UseShellExecute = runAsAdmin,
            WindowStyle = ProcessWindowStyle.Hidden,
            ErrorDialog = false,
            RedirectStandardOutput = !runAsAdmin,
            RedirectStandardError = !runAsAdmin,
            RedirectStandardInput = !runAsAdmin,
        };

        if (!runAsAdmin)
        {
            psi.Environment["KLEVADEPLOY_STORAGE_DIR"] = storageDir;
            psi.Environment["KLEVADEPLOY_DATA_DIR"] = storageDir;
            psi.Environment["KLEVADEPLOY_TEMP_DIR"] = tempDir;
            psi.Environment["TEMP"] = tempDir;
            psi.Environment["TMP"] = tempDir;
        }

        try
        {
            if (File.Exists(executablePath))
            {
                var wd = Path.GetDirectoryName(executablePath);
                if (!string.IsNullOrWhiteSpace(wd) && Directory.Exists(wd))
                    psi.WorkingDirectory = wd;
            }
            else
            {
                psi.WorkingDirectory = tempDir;
            }
        }
        catch { }

        _log.AppendRaw("CMD", BuildTerminalCommandLine(psi.WorkingDirectory, executablePath, arguments, runAsAdmin));

        if (runAsAdmin)
        {
            psi.Verb = "runas";
            _log.Warning("Process is running as admin; live STDOUT/STDERR streaming is not available. Output will be captured after completion when possible.");
        }
        else
        {
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.StandardInputEncoding = Encoding.UTF8;
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        using var _ = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        });

        process.Start();

        Task stdoutTask = Task.CompletedTask;
        Task stderrTask = Task.CompletedTask;
        if (!runAsAdmin)
        {
            Func<string, IEnumerable<string>>? stdoutTransform = null;
            if (LooksLike7Zip(executablePath))
                stdoutTransform = new SevenZipStdoutCompactor().Transform;

            stdoutTask = StreamAndLogAsync(process.StandardOutput, "STDOUT", stdOut, ct, stdoutTransform);
            stderrTask = StreamAndLogAsync(process.StandardError, "STDERR", stdErr, ct, transform: null);
        }

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        if (captureAdminOutputToFile && adminCapturePath is not null)
        {
            try
            {
                if (File.Exists(adminCapturePath))
                {
                    var text = ReadTextWithBomFallback(adminCapturePath);
                    foreach (var line in SplitLines(text))
                    {
                        stdOut.AppendLine(line);
                        _log.AppendRaw("STDOUT", line);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to read captured admin output: {adminCapturePath}", ex);
            }
            finally
            {
                try { File.Delete(adminCapturePath); } catch { }
            }
        }

        var result = new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
        _log.Info($"Process exited with code {result.ExitCode}");
        return result;
    }

    public async Task<ProcessResult> RunPowerShellAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info($"Running PowerShell script (Inline: {isInlineScript}, RunAsAdmin: {runAsAdmin})");

        string arguments;
        if (isInlineScript)
        {
            var tempPs1 = Path.Combine(GetTempDir(), $"KlevaDeploy_{Guid.NewGuid():N}.ps1");
            await File.WriteAllTextAsync(tempPs1, scriptPathOrContent, Encoding.UTF8, ct);
            _log.Info($"Inline PowerShell script written to: {tempPs1}");

            try
            {
                arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{tempPs1}\"";
                return await RunAsync("powershell.exe", arguments, runAsAdmin, ct);
            }
            finally
            {
                try { File.Delete(tempPs1); } catch { }
            }
        }
        
        if (!File.Exists(scriptPathOrContent))
            throw new FileNotFoundException($"PowerShell script not found: {scriptPathOrContent}");

        arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPathOrContent}\"";
        return await RunAsync("powershell.exe", arguments, runAsAdmin, ct);
    }

    public async Task<ProcessResult> RunBatchAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info($"Running Batch script (Inline: {isInlineScript}, RunAsAdmin: {runAsAdmin})");

        if (isInlineScript)
        {
            var tempBatchFile = Path.Combine(GetTempDir(), $"KlevaDeploy_{Guid.NewGuid():N}.bat");
            await File.WriteAllTextAsync(tempBatchFile, scriptPathOrContent, ct);
            _log.Info($"Inline batch script written to: {tempBatchFile}");

            try
            {
                return await RunAsync("cmd.exe", $"/c \"{tempBatchFile}\"", runAsAdmin, ct);
            }
            finally
            {
                try { File.Delete(tempBatchFile); } catch { }
            }
        }

        if (!File.Exists(scriptPathOrContent))
            throw new FileNotFoundException($"Batch script not found: {scriptPathOrContent}");

        return await RunAsync("cmd.exe", $"/c \"{scriptPathOrContent}\"", runAsAdmin, ct);
    }

    public async Task<ProcessResult> RunBashAsync(string scriptPathOrContent, bool isInlineScript, CancellationToken ct = default)
    {
        _log.Info($"Running Bash script (Inline: {isInlineScript})");

        string arguments;
        if (isInlineScript)
        {
            var tempSh = Path.Combine(GetTempDir(), $"KlevaDeploy_{Guid.NewGuid():N}.sh");
            await File.WriteAllTextAsync(tempSh, scriptPathOrContent, Encoding.UTF8, ct);
            _log.Info($"Inline bash script written to: {tempSh}");

            try
            {
                var bashPath = ToBashPath(Path.GetFullPath(tempSh));
                arguments = $"-lc \"bash '{bashPath}'\"";
                return await RunAsync("bash.exe", arguments, runAsAdmin: false, ct);
            }
            finally
            {
                try { File.Delete(tempSh); } catch { }
            }
        }
        
        if (!File.Exists(scriptPathOrContent))
            throw new FileNotFoundException($"Bash script not found: {scriptPathOrContent}");

        var fullPath = Path.GetFullPath(scriptPathOrContent);
        var bashFile = ToBashPath(fullPath);
        arguments = $"-lc \"bash '{bashFile}'\"";
        return await RunAsync("bash.exe", arguments, runAsAdmin: false, ct);
    }

    public async Task<string> EnsureUnrarInstalledAsync(CancellationToken ct = default)
    {
        await UnrarInstallGate.WaitAsync(ct);
        try
        {
            var cached = _cachedUnrarCliPath;
            if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
                return cached;

            var existingValid = await TryResolveValidUnrarWithCleanupAsync(ct);
            if (!string.IsNullOrWhiteSpace(existingValid))
            {
                _cachedUnrarCliPath = existingValid;
                return existingValid;
            }

            var installDir = GetWritableToolsDir();

            var targetFileName = Environment.Is64BitOperatingSystem ? "unrarw64.exe" : "unrarw32.exe";
            var downloadPath = Path.Combine(GetTempDir(), $"KlevaDeploy_{Guid.NewGuid():N}_{targetFileName}");

            var urls = Environment.Is64BitOperatingSystem
                ? new[] { "https://www.rarlab.com/rar/unrarw64.exe" }
                : new[] { "https://www.rarlab.com/rar/unrarw32.exe", "https://www.rarlab.com/rar/unrarw64.exe" };

            try
            {
                _log.Info($"Downloading UnRAR from {urls[0]}");
                await DownloadToFileAsync(urls, downloadPath, ct);

                string? finalCli = null;
                if (await LooksLikeUnrarCliAsync(downloadPath, ct))
                {
                    var stable = Path.Combine(installDir, "UnRAR.exe");
                    _ = await TryCopyOverwriteWithRetriesAsync(downloadPath, stable, ct);
                    if (File.Exists(stable) && await LooksLikeUnrarCliAsync(stable, ct))
                        finalCli = stable;
                }

                finalCli ??= await TryExtractUnrarCliFromSfxAsync(downloadPath, installDir, ct);

                if (string.IsNullOrWhiteSpace(finalCli))
                {
                    var details = TryDescribeDownloadedFile(downloadPath);
                    var isPe = LooksLikePortableExecutable(downloadPath);
                    if (!isPe)
                        throw new InvalidOperationException($"UnRAR download was intercepted and is not an executable:\n{downloadPath}\n\n{details}");

                    throw new InvalidOperationException($"UnRAR package did not produce unrar.exe:\n{downloadPath}\n\n{details}");
                }

                var alias = Path.Combine(installDir, "unrar.exe");
                _ = await TryCopyOverwriteWithRetriesAsync(finalCli, alias, ct);
                if (File.Exists(alias) && await LooksLikeUnrarCliAsync(alias, ct))
                    finalCli = alias;
                else
                {
                    try { if (File.Exists(alias)) File.Delete(alias); } catch { }
                }

                _log.Info($"UnRAR ready: {finalCli}");
                _cachedUnrarCliPath = finalCli;
                return finalCli;
            }
            finally
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); } catch { }
            }
        }
        finally
        {
            UnrarInstallGate.Release();
        }
    }

    private static string ToBashPath(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath))
            return windowsPath;

        var path = windowsPath.Replace('\\', '/');
        if (path.Length >= 2 && path[1] == ':')
        {
            var drive = char.ToLowerInvariant(path[0]);
            var rest = path.Substring(2);
            if (rest.StartsWith("/")) rest = rest.Substring(1);
            return $"/{drive}/{rest}";
        }

        return path;
    }

    private async Task StreamAndLogAsync(StreamReader reader, string level, StringBuilder capture, CancellationToken ct, Func<string, IEnumerable<string>>? transform)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) break;

            pending.Append(buffer, 0, read);

            while (true)
            {
                var idx = IndexOfLineBreak(pending);
                if (idx < 0) break;

                var line = pending.ToString(0, idx).TrimEnd('\r');
                pending.Remove(0, idx + 1);

                if (line.Length == 0) continue;

                capture.AppendLine(line);
                if (transform is null)
                {
                    _log.AppendRaw(level, line);
                }
                else
                {
                    foreach (var outLine in transform(line))
                    {
                        if (string.IsNullOrWhiteSpace(outLine)) continue;
                        _log.AppendRaw(level, outLine);
                    }
                }
            }
        }

        var tail = pending.ToString().TrimEnd('\r', '\n');
        if (!string.IsNullOrWhiteSpace(tail))
        {
            capture.AppendLine(tail);
            if (transform is null)
            {
                _log.AppendRaw(level, tail);
            }
            else
            {
                foreach (var outLine in transform(tail))
                {
                    if (string.IsNullOrWhiteSpace(outLine)) continue;
                    _log.AppendRaw(level, outLine);
                }
            }
        }
    }

    private static bool LooksLike7Zip(string executablePath)
    {
        var name = Path.GetFileName(executablePath);
        return string.Equals(name, "7z.exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "7za.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildStartProcessMessage(string executablePath, string arguments, bool runAsAdmin)
    {
        var exeName = Path.GetFileName(executablePath);
        if (string.IsNullOrWhiteSpace(exeName))
            exeName = executablePath;

        var compact = $"{exeName} {CompactArguments(exeName, arguments)}".Trim();
        return $"Starting process: {compact} (RunAsAdmin: {runAsAdmin})";
    }

    private static string BuildTerminalCommandLine(string? workingDirectory, string executablePath, string arguments, bool runAsAdmin)
    {
        var wd = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory;
        var exe = executablePath;
        if (!Path.IsPathRooted(exe))
            exe = Path.GetFileName(exe) ?? executablePath;

        var args = (arguments ?? string.Empty).Trim();
        var cmd = args.Length == 0
            ? $"PS {wd}> & \"{exe}\""
            : $"PS {wd}> & \"{exe}\" {args}";

        return runAsAdmin ? $"{cmd}  (admin)" : cmd;
    }

    private static string CompactArguments(string exeName, string arguments)
    {
        var args = (arguments ?? string.Empty).Trim();
        if (args.Length == 0) return string.Empty;

        if (string.Equals(exeName, "msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            var msi = TryExtractQuotedValue(args, "/i") ?? TryExtractQuotedValue(args, "/package");
            if (!string.IsNullOrWhiteSpace(msi))
            {
                var file = Path.GetFileName(msi);
                args = args.Replace(msi, file, StringComparison.OrdinalIgnoreCase);
            }

            var log = TryExtractQuotedValue(args, "/L*v") ?? TryExtractQuotedValue(args, "/l*v");
            if (!string.IsNullOrWhiteSpace(log))
            {
                var file = Path.GetFileName(log);
                args = args.Replace(log, file, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (string.Equals(exeName, "7z.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exeName, "7za.exe", StringComparison.OrdinalIgnoreCase))
        {
            var archive = TryExtractQuotedValue(args, "x") ?? TryExtractQuotedValue(args, "e");
            if (!string.IsNullOrWhiteSpace(archive))
            {
                var file = Path.GetFileName(archive);
                args = args.Replace(archive, file, StringComparison.OrdinalIgnoreCase);
            }

            var outDir = TryExtractSwitchQuotedValue(args, "-o");
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                var tail = outDir.TrimEnd('\\', '/');
                var name = Path.GetFileName(tail);
                if (!string.IsNullOrWhiteSpace(name))
                    args = args.Replace(outDir, name, StringComparison.OrdinalIgnoreCase);
            }
        }

        const int max = 160;
        if (args.Length <= max) return args;
        return args.Substring(0, max).TrimEnd() + "…";
    }

    private static string? TryExtractQuotedValue(string args, string token)
    {
        var idx = args.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += token.Length;
        while (idx < args.Length && char.IsWhiteSpace(args[idx])) idx++;
        if (idx >= args.Length) return null;
        if (args[idx] != '"') return null;
        idx++;
        var end = args.IndexOf('"', idx);
        if (end < 0) return null;
        return args.Substring(idx, end - idx);
    }

    private static string? TryExtractSwitchQuotedValue(string args, string switchPrefix)
    {
        var idx = args.IndexOf(switchPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        idx += switchPrefix.Length;
        if (idx >= args.Length) return null;
        if (args[idx] != '"') return null;
        idx++;
        var end = args.IndexOf('"', idx);
        if (end < 0) return null;
        return args.Substring(idx, end - idx);
    }

    private sealed class SevenZipStdoutCompactor
    {
        private bool _inComment;
        private readonly List<string> _comment = new();
        private string _archiveName = string.Empty;
        private bool _emittedHeader;

        public IEnumerable<string> Transform(string line)
        {
            var t = (line ?? string.Empty).TrimEnd();
            if (t.Length == 0) yield break;

            if (_inComment)
            {
                if (t.Trim() == "}")
                {
                    _inComment = false;
                    var setup = _comment.FirstOrDefault(x => x.StartsWith("Setup=", StringComparison.OrdinalIgnoreCase));
                    var silent = _comment.FirstOrDefault(x => x.StartsWith("Silent=", StringComparison.OrdinalIgnoreCase));
                    var overwrite = _comment.FirstOrDefault(x => x.StartsWith("Overwrite=", StringComparison.OrdinalIgnoreCase));
                    var tempMode = _comment.Any(x => string.Equals(x, "TempMode", StringComparison.OrdinalIgnoreCase));
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(setup)) parts.Add(setup);
                    if (!string.IsNullOrWhiteSpace(silent)) parts.Add(silent);
                    if (!string.IsNullOrWhiteSpace(overwrite)) parts.Add(overwrite);
                    if (tempMode) parts.Add("TempMode");
                    if (parts.Count > 0) yield return $"SFX: {string.Join(" ", parts)}";
                    yield break;
                }

                var inner = t.Trim();
                if (inner is "{" or "}") yield break;
                _comment.Add(inner);
                yield break;
            }

            if (t.StartsWith("Comment =", StringComparison.OrdinalIgnoreCase))
            {
                _inComment = true;
                _comment.Clear();
                yield break;
            }

            if (t.StartsWith("Extracting archive:", StringComparison.OrdinalIgnoreCase))
            {
                var raw = t.Substring("Extracting archive:".Length).Trim();
                _archiveName = Path.GetFileName(raw);
                if (string.IsNullOrWhiteSpace(_archiveName))
                    _archiveName = raw;

                if (!_emittedHeader)
                {
                    _emittedHeader = true;
                    yield return string.IsNullOrWhiteSpace(_archiveName) ? "7-Zip: estrazione..." : $"7-Zip: estrazione {_archiveName}...";
                }
                yield break;
            }

            if (t.StartsWith("Everything is Ok", StringComparison.OrdinalIgnoreCase))
            {
                yield return "7-Zip: OK";
                yield break;
            }

            if (t.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Errors:", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("Warnings:", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Can not", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Cannot", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Data error", StringComparison.OrdinalIgnoreCase))
            {
                yield return t;
            }
        }
    }

    private static int IndexOfLineBreak(StringBuilder sb)
    {
        for (var i = 0; i < sb.Length; i++)
        {
            var c = sb[i];
            if (c == '\n' || c == '\r') return i;
        }
        return -1;
    }

    private static string BuildCmdCaptureArguments(string executablePath, string arguments, string outputPath)
    {
        var exe = executablePath.Replace("\"", "\"\"");
        var outPath = outputPath.Replace("\"", "\"\"");

        return string.IsNullOrWhiteSpace(arguments)
            ? $"/c \"\"{exe}\" > \"{outPath}\" 2>&1\""
            : $"/c \"\"{exe}\" {arguments} > \"{outPath}\" 2>&1\"";
    }

    private static string GetStorageDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : overrideDir;
    }

    private static string GetTempDir(string? storageDir = null)
    {
        var root = string.IsNullOrWhiteSpace(storageDir) ? GetStorageDir() : storageDir!;
        return Path.Combine(root, "temp");
    }

    private static string BuildCmdCaptureArguments(string executablePath, string arguments, string outputPath, string storageDir, string tempDir)
    {
        var exe = executablePath.Replace("\"", "\"\"");
        var outPath = outputPath.Replace("\"", "\"\"");
        var dataDir = storageDir.Replace("\"", "\"\"");
        var tmpDir = tempDir.Replace("\"", "\"\"");

        var prefix = $"set \"KLEVADEPLOY_STORAGE_DIR={dataDir}\" & set \"KLEVADEPLOY_DATA_DIR={dataDir}\" & set \"KLEVADEPLOY_TEMP_DIR={tmpDir}\" & set \"TEMP={tmpDir}\" & set \"TMP={tmpDir}\" & cd /d \"{tmpDir}\" & ";

        return string.IsNullOrWhiteSpace(arguments)
            ? $"/c \"{prefix}\"\"{exe}\" > \"{outPath}\" 2>&1\""
            : $"/c \"{prefix}\"\"{exe}\" {arguments} > \"{outPath}\" 2>&1\"";
    }

    private static string ReadTextWithBomFallback(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        using var reader = new StringReader(text);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null) yield break;
            if (line.Length == 0) continue;
            yield return line;
        }
    }

    private static IEnumerable<string> EnumerateUnrarCandidatePaths()
    {
        var storageDir = GetStorageDir();
        var toolsDir = Path.Combine(storageDir, "Tools");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        yield return Path.Combine(toolsDir, "unrar.exe");
        yield return Path.Combine(toolsDir, "UnRAR.exe");
        yield return Path.Combine(toolsDir, "Unrar", "unrar.exe");
        yield return Path.Combine(toolsDir, "Unrar", "UnRAR.exe");

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "WinRAR", "UnRAR.exe");
            yield return Path.Combine(programFiles, "WinRAR", "unrar.exe");
        }
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "WinRAR", "UnRAR.exe");
            yield return Path.Combine(programFilesX86, "WinRAR", "unrar.exe");
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var p in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(p, "unrar.exe");
            yield return Path.Combine(p, "UnRAR.exe");
        }
    }

    private static async Task<string?> TryResolveValidUnrarExecutablePathAsync(CancellationToken ct)
    {
        foreach (var c in EnumerateUnrarCandidatePaths())
        {
            try
            {
                if (!File.Exists(c)) continue;
                if (await LooksLikeUnrarCliAsync(c, ct)) return c;
            }
            catch { }
        }

        return null;
    }

    private static bool LooksLikePortableExecutable(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[2];
            var read = fs.Read(head, 0, head.Length);
            return read == 2 && head[0] == 0x4D && head[1] == 0x5A;
        }
        catch
        {
            return false;
        }
    }

    private static async Task CleanupInvalidUnrarCandidatesAsync(CancellationToken ct)
    {
        string? toolsDir = null;
        try { toolsDir = Path.GetFullPath(Path.Combine(GetStorageDir(), "Tools")); } catch { }

        foreach (var c in EnumerateUnrarCandidatePaths())
        {
            try
            {
                if (!File.Exists(c)) continue;

                var full = Path.GetFullPath(c);
                var underTools = toolsDir is not null && full.StartsWith(toolsDir, StringComparison.OrdinalIgnoreCase);
                if (!underTools) continue;

                if (await LooksLikeUnrarCliAsync(full, ct)) continue;

                try { File.Delete(full); } catch { }
            }
            catch { }
        }
    }

    private static async Task<string?> TryResolveValidUnrarWithCleanupAsync(CancellationToken ct)
    {
        var existing = await TryResolveValidUnrarExecutablePathAsync(ct);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        await CleanupInvalidUnrarCandidatesAsync(ct);
        return await TryResolveValidUnrarExecutablePathAsync(ct);
    }

    private static string GetWritableToolsDir()
    {
        var toolsDir = Path.Combine(GetStorageDir(), "Tools");
        Directory.CreateDirectory(toolsDir);
        return toolsDir;
    }

    private async Task<string?> TryExtractUnrarCliFromSfxAsync(string sfxPath, string outputDir, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(sfxPath)) return null;
            Directory.CreateDirectory(outputDir);

            var result = await RunSfxAsync(sfxPath, outputDir, $"-s -d\"{outputDir}\"", ct);
            if (result.ExitCode != 0)
                _log.Warning($"UnRAR SFX exited with code {result.ExitCode}");

            var timeout = Stopwatch.StartNew();
            while (timeout.Elapsed < TimeSpan.FromSeconds(30))
            {
                ct.ThrowIfCancellationRequested();

                var candidates = new[]
                {
                    Path.Combine(outputDir, "unrar.exe"),
                    Path.Combine(outputDir, "UnRAR.exe"),
                    Path.Combine(outputDir, "Unrar", "unrar.exe"),
                    Path.Combine(outputDir, "Unrar", "UnRAR.exe"),
                };

                foreach (var c in candidates)
                {
                    try
                    {
                        if (!File.Exists(c)) continue;
                        if (await LooksLikeUnrarCliAsync(c, ct)) return c;
                    }
                    catch { }
                }

                await Task.Delay(200, ct);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ProcessResult> RunSfxAsync(string sfxPath, string workingDir, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sfxPath,
            Arguments = args,
            WorkingDirectory = workingDir,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            ErrorDialog = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var p = new Process { StartInfo = psi };
        p.Start();

        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stdout)) _log.AppendRaw("STDOUT", stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(stderr)) _log.AppendRaw("STDERR", stderr.TrimEnd());

        return new ProcessResult(p.ExitCode, stdout, stderr);
    }

    private static async Task<bool> LooksLikeUnrarCliAsync(string unrarPath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(unrarPath)) return false;

            var info = FileVersionInfo.GetVersionInfo(unrarPath);
            var product = (info.ProductName ?? string.Empty).Trim();
            var desc = (info.FileDescription ?? string.Empty).Trim();

            if (product.Contains("WinRAR", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("WinRAR", StringComparison.OrdinalIgnoreCase))
                return false;

            if (product.Contains("UnRAR", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("UnRAR", StringComparison.OrdinalIgnoreCase) ||
                product.Contains("UNRAR", StringComparison.OrdinalIgnoreCase) ||
                desc.Contains("UNRAR", StringComparison.OrdinalIgnoreCase))
                return true;

            await using var fs = new FileStream(unrarPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var probeLen = (int)Math.Min(2 * 1024 * 1024, fs.Length);
            if (probeLen <= 0) return false;

            var bytes = new byte[probeLen];
            var read = await fs.ReadAsync(bytes.AsMemory(0, probeLen), ct);
            if (read <= 0) return false;

            var text = Encoding.ASCII.GetString(bytes, 0, read);
            if (text.Contains("WinRAR", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("UNRAR", StringComparison.OrdinalIgnoreCase) || text.Contains("UnRAR", StringComparison.OrdinalIgnoreCase))
                return true;

            using var probe = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = unrarPath,
                    Arguments = "-?",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };

            try { probe.Start(); }
            catch { return false; }

            var stdoutTask = probe.StandardOutput.ReadToEndAsync();
            var stderrTask = probe.StandardError.ReadToEndAsync();
            var exitTask = probe.WaitForExitAsync(ct);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15), ct);
            var finished = await Task.WhenAny(exitTask, timeoutTask);
            if (finished != exitTask)
            {
                try { if (!probe.HasExited) probe.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            var outText = (await stdoutTask) + "\n" + (await stderrTask);
            if (outText.Contains("WinRAR", StringComparison.OrdinalIgnoreCase)) return false;
            return outText.Contains("UNRAR", StringComparison.OrdinalIgnoreCase) ||
                   outText.Contains("UnRAR", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadToFileAsync(IEnumerable<string> urls, string destinationPath, CancellationToken ct)
    {
        using var http = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli,
        });
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) KlevaDeploy/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("*/*");

        Exception? last = null;
        foreach (var url in urls)
        {
            string? tmp = null;
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                tmp = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var inStream = await resp.Content.ReadAsStreamAsync(ct))
                await using (var outStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await inStream.CopyToAsync(outStream, 1024 * 128, ct);
                }

                TryRemoveZoneIdentifier(tmp);
                var moved = await TryMoveOverwriteWithRetriesAsync(tmp, destinationPath, ct);
                if (!moved)
                {
                    try { File.Delete(tmp); } catch { }
                    throw new IOException($"Unable to replace destination file after multiple attempts: {destinationPath}");
                }
                return;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(tmp))
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
                last = ex;
            }
        }

        throw new InvalidOperationException($"Download failed: {destinationPath}", last);
    }

    private static async Task<bool> TryMoveOverwriteWithRetriesAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        var delay = 150;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            await Task.Delay(delay, ct);
            delay = Math.Min(delay * 2, 1500);
        }

        return false;
    }

    private static async Task<bool> TryCopyOverwriteWithRetriesAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        var delay = 150;
        for (var attempt = 0; attempt < 25; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            await Task.Delay(delay, ct);
            delay = Math.Min(delay * 2, 1500);
        }

        return false;
    }

    private static string TryDescribeDownloadedFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return "File does not exist after download.";
            var size = new FileInfo(path).Length;

            var head = new byte[256];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var read = fs.Read(head, 0, head.Length);
                if (read <= 0) return $"Downloaded file size: {size} bytes (empty read).";

                var raw = Encoding.ASCII.GetString(head, 0, read).Replace("\0", string.Empty);
                var sb = new StringBuilder();
                foreach (var c in raw)
                {
                    if (c == '\r' || c == '\n' || (c >= 32 && c <= 126))
                        sb.Append(c);
                    if (sb.Length >= 200) break;
                }
                var text = sb.ToString();

                var mz = read >= 2 && head[0] == 0x4D && head[1] == 0x5A;
                return $"Downloaded file size: {size} bytes\nStarts with MZ header: {mz}\nHeader text preview: {text}";
            }
        }
        catch (Exception ex)
        {
            return $"Unable to inspect downloaded file: {ex.Message}";
        }
    }

    private static void TryRemoveZoneIdentifier(string filePath)
    {
        try
        {
            var ads = filePath + ":Zone.Identifier";
            if (File.Exists(ads))
                File.Delete(ads);
        }
        catch { }
    }
}
