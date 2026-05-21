using System.Diagnostics;
using DeploymentApp.Services.Interfaces;

namespace DeploymentApp.Services;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    private readonly ILogService _log;

    public ProcessExecutionService(ILogService log) => _log = log;

    public async Task<ProcessResult> RunAsync(string executablePath, string arguments, CancellationToken ct = default)
    {
        _log.Info($"Starting process: {executablePath} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdOut = new System.Text.StringBuilder();
        var stdErr = new System.Text.StringBuilder();

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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        var result = new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
        _log.Info($"Process exited with code {result.ExitCode}");
        return result;
    }
}
