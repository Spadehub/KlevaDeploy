namespace DeploymentApp.Services.Interfaces;

public record LogEntry(DateTime Timestamp, string Level, string Message);

public interface ILogService
{
    IReadOnlyList<LogEntry> Entries { get; }
    event EventHandler<LogEntry>? LogAdded;
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);
    void AppendRaw(string level, string message);
}
