using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private readonly ILogService _log;

    public ProcessExecutionService(ILogService log) => _log = log;

    public async Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default)
    {
        _log.Info($"Starting process: {executablePath} {arguments} (RunAsAdmin: {runAsAdmin})");

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = runAsAdmin, // Must be true for "runas"
            RedirectStandardOutput = !runAsAdmin,
            RedirectStandardError = !runAsAdmin,
        };

        if (runAsAdmin)
        {
            psi.Verb = "runas";
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

        if (!runAsAdmin)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                stdOut.AppendLine(e.Data);
                _log.AppendRaw("STDOUT", e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                stdErr.AppendLine(e.Data);
                _log.AppendRaw("STDERR", e.Data);
            };
        }

        process.Start();

        if (!runAsAdmin)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        await process.WaitForExitAsync(ct);

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
}
