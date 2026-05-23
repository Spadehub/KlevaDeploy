using KlevaDeploy.Services;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Tests;

public sealed class ProcessExecutionServiceTests
{
    [Fact]
    public async Task RunAsync_EmitsStdoutLines()
    {
        var log = new CapturingLogService();
        var svc = new ProcessExecutionService(log);

        var result = await svc.RunAsync("cmd.exe", "/c echo hello", runAsAdmin: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello", result.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(log.Entries, e => e.Level == "STDOUT" && e.Message.Contains("hello", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingLogService : ILogService
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public event EventHandler<LogEntry>? LogAdded;

        public void Info(string message) => AppendRaw("INFO", message);
        public void Warning(string message) => AppendRaw("WARN", message);
        public void Error(string message, Exception? ex = null) =>
            AppendRaw("ERROR", ex is null ? message : $"{message} | {ex.Message}");

        public void AppendRaw(string level, string message)
        {
            var entry = new LogEntry(DateTime.Now, level, message);
            _entries.Add(entry);
            LogAdded?.Invoke(this, entry);
        }
    }
}
