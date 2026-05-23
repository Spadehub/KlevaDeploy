using System.Collections;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.ViewModels;

public sealed partial class LogViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;

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

        // Populate with existing entries
        foreach (var entry in entries)
            LogEntries.Add(entry);

        LogText = string.Join(Environment.NewLine, LogEntries.Select(FormatLogEntry));

        LogEntries.CollectionChanged += (_, __) =>
            LogText = string.Join(Environment.NewLine, LogEntries.Select(FormatLogEntry));

        foreach (var entry in entries)
        {
            if (TryFormatTerminalLine(entry, out var terminalLine))
                TerminalLines.Add(terminalLine);
        }

        TerminalText = string.Join(Environment.NewLine, TerminalLines);

        TerminalLines.CollectionChanged += (_, __) =>
            TerminalText = string.Join(Environment.NewLine, TerminalLines);

        // Subscribe to new entries
        logService.LogAdded += (_, entry) =>
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                LogEntries.Add(entry);

                if (TryFormatTerminalLine(entry, out var terminalLine))
                {
                    TerminalLines.Add(terminalLine);
                    TrimTerminal();
                }
            });
        };
    }

    public void ClearTerminal() => TerminalLines.Clear();

    [RelayCommand]
    private void CopyTerminalAll()
    {
        if (TerminalLines.Count == 0) return;
        _clipboard.SetText(string.Join(Environment.NewLine, TerminalLines));
    }

    [RelayCommand]
    private void CopyTerminalLine(int selectionStart)
    {
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
        if (entry.Level == "STDOUT")
        {
            line = entry.Message;
            return true;
        }

        if (entry.Level == "STDERR")
        {
            line = $"[stderr] {entry.Message}";
            return true;
        }

        if (entry.Level == "WARN")
        {
            line = $"> [warn] {entry.Message}";
            return true;
        }

        if (entry.Level == "ERROR")
        {
            line = $"> [error] {entry.Message}";
            return true;
        }

        if (entry.Level == "INFO" && entry.Message.StartsWith("Starting process:", StringComparison.OrdinalIgnoreCase))
        {
            line = $"> {entry.Message}";
            return true;
        }

        if (entry.Level == "INFO" && entry.Message.StartsWith("Process exited with code", StringComparison.OrdinalIgnoreCase))
        {
            line = $"> {entry.Message}";
            return true;
        }

        line = string.Empty;
        return false;
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

        if (TerminalLines.Count <= maxLines) return;
        while (TerminalLines.Count > trimTo)
        {
            TerminalLines.RemoveAt(0);
        }
    }
}
