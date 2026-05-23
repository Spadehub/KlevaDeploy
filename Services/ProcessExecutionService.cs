using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private readonly ILogService _log;

    public ProcessExecutionService(ILogService log) => _log = log;

    public async Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info($"Starting process: {executablePath} {arguments} (RunAsAdmin: {runAsAdmin})");

        string? adminCapturePath = null;
        var captureAdminOutputToFile = runAsAdmin;

        if (captureAdminOutputToFile)
        {
            adminCapturePath = Path.Combine(Path.GetTempPath(), $"KlevaDeploy_{Guid.NewGuid():N}.out.txt");
        }

        var psi = new ProcessStartInfo
        {
            FileName = captureAdminOutputToFile ? "cmd.exe" : executablePath,
            Arguments = captureAdminOutputToFile
                ? BuildCmdCaptureArguments(executablePath, arguments, adminCapturePath!)
                : arguments,
            CreateNoWindow = true,
            UseShellExecute = runAsAdmin, // Must be true for "runas"
            RedirectStandardOutput = !runAsAdmin,
            RedirectStandardError = !runAsAdmin,
        };

        if (runAsAdmin)
        {
            psi.Verb = "runas";
            _log.Warning("Process is running as admin; live STDOUT/STDERR streaming is not available. Output will be captured after completion when possible.");
        }
        else
        {
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
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
            catch { /* ignore */ }
        });

        process.Start();

        Task stdoutTask = Task.CompletedTask;
        Task stderrTask = Task.CompletedTask;
        if (!runAsAdmin)
        {
            // Stream output as it arrives (not line-event based), so we don't lose carriage-return progress output.
            stdoutTask = StreamAndLogAsync(process.StandardOutput, "STDOUT", stdOut, ct);
            stderrTask = StreamAndLogAsync(process.StandardError, "STDERR", stdErr, ct);
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

    public async Task<string> ExtractZipToTempAsync(string zipPath, CancellationToken ct = default)
    {
        _log.Info($"Extracting ZIP archive: {zipPath}");

        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException($"ZIP file not found: {zipPath}");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"KlevaDeploy_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempDir), ct);

        _log.Info($"ZIP extracted to: {tempDir}");
        return tempDir;
    }

    public async Task<ProcessResult> RunPowerShellAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info($"Running PowerShell script (Inline: {isInlineScript}, RunAsAdmin: {runAsAdmin})");

        string arguments;
        if (isInlineScript)
        {
            // Inline script: use -Command parameter
            var escapedScript = scriptPathOrContent.Replace("\"", "`\"");
            arguments = $"-ExecutionPolicy Bypass -NoProfile -Command \"{escapedScript}\"";
        }
        else
        {
            // File-based script: use -File parameter
            if (!File.Exists(scriptPathOrContent))
            {
                throw new FileNotFoundException($"PowerShell script not found: {scriptPathOrContent}");
            }
            arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPathOrContent}\"";
        }

        return await RunAsync("powershell.exe", arguments, runAsAdmin, ct);
    }

    public async Task<ProcessResult> RunBatchAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info($"Running Batch script (Inline: {isInlineScript}, RunAsAdmin: {runAsAdmin})");

        if (isInlineScript)
        {
            // For inline batch scripts, write to a temp file first
            var tempBatchFile = Path.Combine(Path.GetTempPath(), $"KlevaDeploy_{Guid.NewGuid():N}.bat");
            await File.WriteAllTextAsync(tempBatchFile, scriptPathOrContent, ct);
            _log.Info($"Inline batch script written to: {tempBatchFile}");

            try
            {
                return await RunAsync("cmd.exe", $"/c \"{tempBatchFile}\"", runAsAdmin, ct);
            }
            finally
            {
                // Clean up temp file
                try { File.Delete(tempBatchFile); } catch { /* Ignore cleanup errors */ }
            }
        }
        else
        {
            // File-based script
            if (!File.Exists(scriptPathOrContent))
            {
                throw new FileNotFoundException($"Batch script not found: {scriptPathOrContent}");
            }
            return await RunAsync("cmd.exe", $"/c \"{scriptPathOrContent}\"", runAsAdmin, ct);
        }
    }

    public async Task<ProcessResult> RunBashAsync(string scriptPathOrContent, bool isInlineScript, CancellationToken ct = default)
    {
        _log.Info($"Running Bash script (Inline: {isInlineScript})");

        string arguments;
        if (isInlineScript)
        {
            var escapedScript = scriptPathOrContent.Replace("\"", "\\\"");
            arguments = $"-lc \"{escapedScript}\"";
        }
        else
        {
            if (!File.Exists(scriptPathOrContent))
            {
                throw new FileNotFoundException($"Bash script not found: {scriptPathOrContent}");
            }

            var fullPath = Path.GetFullPath(scriptPathOrContent);
            var bashPath = ToBashPath(fullPath);
            arguments = $"-lc \"bash '{bashPath}'\"";
        }

        return await RunAsync("bash.exe", arguments, runAsAdmin: false, ct);
    }

    private static string ToBashPath(string windowsPath)
    {
        if (string.IsNullOrWhiteSpace(windowsPath))
        {
            return windowsPath;
        }

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

    private async Task StreamAndLogAsync(StreamReader reader, string level, StringBuilder capture, CancellationToken ct)
    {
        // Note: this method logs "lines" split on '\n' OR '\r' (some tools print progress using carriage returns).
        var buffer = new char[4096];
        var pending = new StringBuilder();

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0) break;

            pending.Append(buffer, 0, read);

            // Extract completed lines.
            while (true)
            {
                var idx = IndexOfLineBreak(pending);
                if (idx < 0) break;

                var line = pending.ToString(0, idx).TrimEnd('\r');
                pending.Remove(0, idx + 1);

                // Skip empty fragments caused by "\r\n" handling (after removing '\r' we may see a leading '\n').
                if (line.Length == 0) continue;

                capture.AppendLine(line);
                _log.AppendRaw(level, line);
            }
        }

        // Flush remainder.
        var tail = pending.ToString().TrimEnd('\r', '\n');
        if (!string.IsNullOrWhiteSpace(tail))
        {
            capture.AppendLine(tail);
            _log.AppendRaw(level, tail);
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
}
