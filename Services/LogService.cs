using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.Services;

public sealed class LogService : ILogService
{
    private readonly List<LogEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) { return _entries.AsReadOnly(); } }
    }

    public event EventHandler<LogEntry>? LogAdded;

    public void Info(string message) => AppendRaw("INFO", message);
    public void Warning(string message) => AppendRaw("WARN", message);
    public void Error(string message, Exception? ex = null) =>
        AppendRaw("ERROR", ex is null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    public void AppendRaw(string level, string message)
    {
        var sanitized = LogSanitizer.Sanitize(level, message);
        if (string.IsNullOrWhiteSpace(sanitized))
            return;

        var entry = new LogEntry(DateTime.Now, level, sanitized);
        lock (_lock) { _entries.Add(entry); }
        LogAdded?.Invoke(this, entry);
    }
}
