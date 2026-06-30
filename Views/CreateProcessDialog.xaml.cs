using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class CreateProcessDialog : Window
{
    private readonly CreateProcessViewModel _viewModel;

    public bool IsDarkTheme
    {
        get
        {
            try
            {
                var dicts = Application.Current?.Resources?.MergedDictionaries;
                if (dicts is null || dicts.Count == 0) return true;

                var themeDict = dicts.FirstOrDefault(d =>
                    d.Source is not null &&
                    (d.Source.OriginalString.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                     d.Source.OriginalString.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)));

                var src = themeDict?.Source?.OriginalString ?? string.Empty;
                if (src.Contains("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase)) return false;
                if (src.Contains("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
            }

            return true;
        }
    }

    public ScrollViewer LayoutContentScrollViewer => ContentScrollViewer;

    public CreateProcessDialog(CreateProcessViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += (_, _) => BeginSlideInAnimation();

        // Monitor property changes to close dialog when commands complete
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CreateProcessViewModel.DialogResult))
            {
                if (_viewModel.DialogResult is null)
                {
                    return;
                }

                DialogResult = _viewModel.DialogResult;
                Close();
            }
        };
    }

    private void BeginSlideInAnimation()
    {
        var finalLeft = Left;
        var offset = ActualWidth > 0 ? ActualWidth : Width;

        Left = finalLeft + offset;
        Opacity = 0;

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        var slide = new DoubleAnimation
        {
            To = finalLeft,
            Duration = TimeSpan.FromSeconds(0.3),
            EasingFunction = easeOut
        };

        var fade = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromSeconds(0.2),
            EasingFunction = easeOut
        };

        BeginAnimation(LeftProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void CreateSubProcess_Click(object sender, RoutedEventArgs e)
    {
        var subVm = _viewModel.CreateChildViewModel();
        subVm.InitializeNew();

        var dlg = new CreateProcessDialog(subVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        if (dlg.ShowDialog() != true) return;
        if (subVm.CreatedProcess is null) return;

        _viewModel.SubProcesses.Add(new CreateProcessViewModel.SubProcessItem { Process = subVm.CreatedProcess });
    }

    private void EditSubProcess_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CreateProcessViewModel.SubProcessItem item)
            return;

        var subVm = _viewModel.CreateChildViewModel();
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

    private void OpenScriptEditor_Click(object sender, RoutedEventArgs e)
    {
        var editorVm = new ScriptEditorViewModel(_viewModel);
        var editor = new ScriptEditorWindow(editorVm)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        editor.ShowDialog();
    }
}
