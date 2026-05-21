namespace DeploymentApp.Services.Interfaces;

public record ProcessResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessExecutionService
{
    /// <summary>
    /// Runs an executable with arguments. Always uses CreateNoWindow=true
    /// and redirects stdout/stderr. Logs output via ILogService.
    /// </summary>
    Task<ProcessResult> RunAsync(string executablePath, string arguments, CancellationToken ct = default);
}
