namespace KlevaDeploy.Services.Interfaces;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessExecutionService
{
    /// <summary>
    /// Runs an executable with arguments. Always uses CreateNoWindow=true
    /// and redirects stdout/stderr. Logs output via ILogService.
    /// </summary>
    Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default);

    /// <summary>
    /// Extracts a ZIP archive to a temporary directory and returns the extraction path.
    /// </summary>
    Task<string> ExtractZipToTempAsync(string zipPath, CancellationToken ct = default);

    /// <summary>
    /// Runs a PowerShell script (either from file or inline content).
    /// </summary>
    Task<ProcessResult> RunPowerShellAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default);

    /// <summary>
    /// Runs a batch/CMD script (either from file or inline content).
    /// </summary>
    Task<ProcessResult> RunBatchAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default);
}
