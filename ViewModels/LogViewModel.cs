using System.Collections;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.ViewModels;

public sealed partial class LogViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private readonly Queue<string> _logLines = new();
    private readonly Queue<string> _terminalLines = new();
    private readonly StringBuilder _logTextBuilder = new();
    private readonly StringBuilder _terminalTextBuilder = new();
    private int _flushScheduled;

    [ObservableProperty]
    private string logText = string.Empty;

    [ObservableProperty]
    private string terminalText = string.Empty;

    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<string> TerminalLines { get; } = new();

    public LogViewModel(ILogService logService, IClipboardService clipboard)
    {
        _clipboard = clipboard;

        var entries = logService.Entries;

        foreach (var entry in entries)
        {
            if (ShouldIncludeInLog(entry))
            {
                LogEntries.Add(entry);
                _logLines.Enqueue(FormatLogEntry(entry));
            }

            if (TryFormatTerminalLine(entry, out var terminalLine))
            {
                TerminalLines.Add(terminalLine);
                _terminalLines.Enqueue(terminalLine);
            }
        }

        RebuildTextBuilders();

        logService.LogAdded += (_, entry) =>
        {
            _pending.Enqueue(entry);

            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher is null)
            {
                FlushPending();
                return;
            }

            if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                dispatcher.BeginInvoke(FlushPending);
        };
    }

    public void ClearTerminal()
    {
        TerminalLines.Clear();
        _terminalLines.Clear();
        _terminalTextBuilder.Clear();
        TerminalText = string.Empty;
    }

    [RelayCommand]
    private void CopyTerminalAll()
    {
        if (TerminalLines.Count == 0) return;
        _clipboard.SetText(string.Join(Environment.NewLine, TerminalLines));
    }

    [RelayCommand]
    private void CopyTerminalLine(int selectionStart)
    {
        if (selectionStart < 0) return;
        var line = TryExtractLine(TerminalText, selectionStart);
        if (line is null) return;
        _clipboard.SetText(line);
    }

    [RelayCommand]
    private void CopyTerminalSelected(IList? selectedItems)
    {
        if (selectedItems is null || selectedItems.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in selectedItems)
        {
            if (item is not string line) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(line);
        }

        if (sb.Length == 0) return;
        _clipboard.SetText(sb.ToString());
    }

    [RelayCommand]
    private void CopyLogAll()
    {
        if (LogEntries.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var entry in LogEntries)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(FormatLogEntry(entry));
        }

        if (sb.Length == 0) return;
        _clipboard.SetText(sb.ToString());
    }

    [RelayCommand]
    private void CopyLogLine(int selectionStart)
    {
        if (selectionStart < 0) return;
        var line = TryExtractLine(LogText, selectionStart);
        if (line is null) return;
        _clipboard.SetText(line);
    }

    [RelayCommand]
    private void CopyLogSelected(IList? selectedItems)
    {
        if (selectedItems is null || selectedItems.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var item in selectedItems)
        {
            if (item is not LogEntry entry) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(FormatLogEntry(entry));
        }

        if (sb.Length == 0) return;
        _clipboard.SetText(sb.ToString());
    }

    private static bool TryFormatTerminalLine(LogEntry entry, out string line)
    {
        if (IsProgressMarker(entry))
        {
            line = string.Empty;
            return false;
        }

        if (entry.Level == "CMD")
        {
            line = entry.Message;
            return true;
        }

        if (entry.Level == "STDOUT")
        {
            line = entry.Message;
            return true;
        }

        if (entry.Level == "STDERR")
        {
            line = entry.Message;
            return true;
        }

        line = string.Empty;
        return false;
    }

    private static bool ShouldIncludeInLog(LogEntry entry)
    {
        if (IsProgressMarker(entry)) return false;
        return entry.Level is "INFO" or "WARN" or "ERROR";
    }

    private static bool IsProgressMarker(LogEntry entry)
    {
        if (entry.Level is not ("STDOUT" or "STDERR")) return false;
        return entry.Message.StartsWith("KLEVADEPLOY_PROGRESS:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatLogEntry(LogEntry entry) =>
        $"{entry.Timestamp:HH:mm:ss}\t{entry.Level}\t{entry.Message}";

    private static string? TryExtractLine(string text, int charIndex)
    {
        if (string.IsNullOrEmpty(text)) return null;

        if (charIndex < 0) charIndex = 0;
        if (charIndex > text.Length) charIndex = text.Length;

        var start = text.LastIndexOf('\n', Math.Max(0, charIndex - 1));
        start = start < 0 ? 0 : start + 1;

        var end = text.IndexOf('\n', charIndex);
        if (end < 0) end = text.Length;

        var line = text.Substring(start, end - start).TrimEnd('\r');
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }

    private void TrimTerminal()
    {
        const int maxLines = 2000;
        const int trimTo = 1500;

        if (TerminalLines.Count <= maxLines && _terminalLines.Count <= maxLines) return;
        while (TerminalLines.Count > trimTo)
        {
            TerminalLines.RemoveAt(0);
        }

        while (_terminalLines.Count > trimTo)
            _terminalLines.Dequeue();

        RebuildTerminalText();
    }

    private void FlushPending()
    {
        try
        {
            var wroteLog = false;
            var wroteTerminal = false;

            while (_pending.TryDequeue(out var entry))
            {
                if (ShouldIncludeInLog(entry))
                {
                    LogEntries.Add(entry);
                    var logLine = FormatLogEntry(entry);
                    _logLines.Enqueue(logLine);
                    wroteLog = true;
                }

                if (TryFormatTerminalLine(entry, out var terminalLine))
                {
                    TerminalLines.Add(terminalLine);
                    _terminalLines.Enqueue(terminalLine);
                    wroteTerminal = true;
                }
            }

            if (wroteTerminal)
                TrimTerminal();

            const int maxLogLines = 5000;
            while (_logLines.Count > maxLogLines)
            {
                _logLines.Dequeue();
                if (LogEntries.Count > 0) LogEntries.RemoveAt(0);
            }

            if (wroteLog)
                RebuildLogText();

            if (wroteTerminal)
                RebuildTerminalText();
        }
        finally
        {
            Interlocked.Exchange(ref _flushScheduled, 0);
            if (!_pending.IsEmpty)
            {
                var dispatcher = App.Current?.Dispatcher;
                if (dispatcher is null)
                    FlushPending();
                else if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
                    dispatcher.BeginInvoke(FlushPending);
            }
        }
    }

    private void RebuildTextBuilders()
    {
        RebuildLogText();
        RebuildTerminalText();
    }

    private void RebuildLogText()
    {
        _logTextBuilder.Clear();
        var first = true;
        foreach (var line in _logLines)
        {
            if (!first) _logTextBuilder.AppendLine();
            _logTextBuilder.Append(line);
            first = false;
        }

        LogText = _logTextBuilder.ToString();
    }

    private void RebuildTerminalText()
    {
        _terminalTextBuilder.Clear();
        var first = true;
        foreach (var line in _terminalLines)
        {
            if (!first) _terminalTextBuilder.AppendLine();
            _terminalTextBuilder.Append(line);
            first = false;
        }

        TerminalText = _terminalTextBuilder.ToString();
    }
}
