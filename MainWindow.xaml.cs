using System.Collections.Specialized;
using System.Windows;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        if (vm.LogViewModel.TerminalLines is INotifyCollectionChanged terminalLines)
        {
            terminalLines.CollectionChanged += (_, __) =>
            {
                Dispatcher.BeginInvoke(() => TerminalOutputText.ScrollToEnd());
            };
        }

        if (vm.LogViewModel.LogEntries is INotifyCollectionChanged logEntries)
        {
            logEntries.CollectionChanged += (_, __) =>
            {
                Dispatcher.BeginInvoke(() => LogOutputText.ScrollToEnd());
            };
        }

        DragRegion.MouseRightButtonUp += (_, e) =>
            SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));

        BtnMinimize.Click += (_, _) => SystemCommands.MinimizeWindow(this);
        BtnMaximize.Click += (_, _) => ToggleMaximizeRestore();
        BtnClose.Click += (_, _) => SystemCommands.CloseWindow(this);

        StateChanged += (_, _) => UpdateMaximizeGlyph();
        UpdateMaximizeGlyph();
    }

    private void ToggleMaximizeRestore()
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void UpdateMaximizeGlyph() =>
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\u2752" : "\u25A1";
}
