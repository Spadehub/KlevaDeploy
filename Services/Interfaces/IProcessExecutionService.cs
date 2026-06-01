namespace KlevaDeploy.Services.Interfaces;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessExecutionService
{
    Task<ProcessResult> RunAsync(string executablePath, string arguments, bool runAsAdmin = false, CancellationToken ct = default);

    Task<string> Ensure7ZipInstalledAsync(CancellationToken ct = default);

    Task<string> EnsureUnrarInstalledAsync(CancellationToken ct = default);

    Task<ProcessResult> RunPowerShellAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default);

    Task<ProcessResult> RunBatchAsync(string scriptPathOrContent, bool isInlineScript, bool runAsAdmin = false, CancellationToken ct = default);

    Task<ProcessResult> RunBashAsync(string scriptPathOrContent, bool isInlineScript, CancellationToken ct = default);
}
