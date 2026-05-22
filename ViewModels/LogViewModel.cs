using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KlevaDeploy.Services.Interfaces;

namespace KlevaDeploy.ViewModels;

public sealed partial class LogViewModel : ObservableObject
{
    public ObservableCollection<LogEntry> LogEntries { get; } = new();

    public LogViewModel(ILogService logService)
    {
        // Populate with existing entries
        foreach (var entry in logService.Entries)
            LogEntries.Add(entry);

        // Subscribe to new entries
        logService.LogAdded += (_, entry) =>
        {
            App.Current.Dispatcher.Invoke(() => LogEntries.Add(entry));
        };
    }
}
