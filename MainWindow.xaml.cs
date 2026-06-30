using System.Collections.Specialized;
using System.Windows;
using KlevaDeploy.ViewModels;
using KlevaDeploy.Views;

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

    private void SubProcessMenuButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.Button btn) return;
        var cm = btn.ContextMenu;
        if (cm is null) return;
        cm.PlacementTarget = btn;
        cm.IsOpen = true;
    }

    private void CreateSubProcessFromEditor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreateProcessViewModel parentVm) return;

        var subVm = parentVm.CreateChildViewModel();
        subVm.InitializeNew();

        var dlg = new CreateProcessDialog(subVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (dlg.ShowDialog() != true) return;
        if (subVm.CreatedProcess is null) return;

        parentVm.SubProcesses.Add(new CreateProcessViewModel.SubProcessItem { Process = subVm.CreatedProcess });
    }

    private void EditSubProcessFromEditor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreateProcessViewModel.SubProcessItem item) return;
        if (DataContext is not MainViewModel mainVm) return;

        var parentVm = mainVm.CreateProcessViewModel;
        var subVm = parentVm.CreateChildViewModel();

        if (item.Process is not null)
        {
            subVm.InitializeForEdit(item.Process);
        }
        else
        {
            static Models.ProcessKind InferKind(string path)
            {
                var ext = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
                return ext switch
                {
                    ".ps1" => Models.ProcessKind.PowerShellScript,
                    ".bat" => Models.ProcessKind.BatchScript,
                    ".cmd" => Models.ProcessKind.BatchScript,
                    ".sh" => Models.ProcessKind.BashScript,
                    ".reg" => Models.ProcessKind.RegistryFile,
                    _ => Models.ProcessKind.Installer
                };
            }

            subVm.InitializeNew();
            subVm.ProcessName = item.Name ?? string.Empty;
            subVm.SelectedProcessKind = InferKind(item.RelativePath);
            subVm.InstallerSourceMode = Models.InstallerSourceMode.StaticLocal;
            subVm.FilePath = item.RelativePath ?? string.Empty;
            subVm.Arguments = item.Arguments ?? string.Empty;
            subVm.RunAsAdmin = item.RunAsAdminEffective == true;
        }

        var dlg = new CreateProcessDialog(subVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (dlg.ShowDialog() != true) return;
        if (subVm.CreatedProcess is null) return;

        item.Process = subVm.CreatedProcess;
        item.SubProcess.Name = string.Empty;
        item.SubProcess.RelativePath = string.Empty;
        item.SubProcess.Arguments = string.Empty;
        item.SubProcess.RunAsAdmin = null;
    }

    private void OpenScriptEditorFromEditor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreateProcessViewModel vm) return;

        var editorVm = new ScriptEditorViewModel(vm);
        var editor = new ScriptEditorWindow(editorVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        editor.ShowDialog();
    }
}
