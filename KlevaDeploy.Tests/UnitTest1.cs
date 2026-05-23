using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KlevaDeploy.Converters;
using KlevaDeploy.Models;
using KlevaDeploy.ViewModels;
using KlevaDeploy.Views;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KlevaDeploy.Tests;

public sealed class MainWindowRegressionTests
{
    [Fact]
    public void MainWindow_DoesNotApplyProcessCardToggleButtonStyleToButton()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var mainWindowXamlPath = Path.Combine(repoRoot, "MainWindow.xaml");

        Assert.True(File.Exists(mainWindowXamlPath), $"File not found: {mainWindowXamlPath}");

        var xaml = File.ReadAllText(mainWindowXamlPath);
        var hasInvalidUsage = Regex.IsMatch(
            xaml,
            "<Button\\b[^>]*\\bStyle=\"\\{StaticResource\\s+ProcessCardToggleButton\\}\"",
            RegexOptions.IgnoreCase);

        Assert.False(hasInvalidUsage);
    }

    [Fact]
    public void MainWindow_SelectedQueue_HasDragDropEnabled()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var mainWindowXamlPath = Path.Combine(repoRoot, "MainWindow.xaml");

        Assert.True(File.Exists(mainWindowXamlPath), $"File not found: {mainWindowXamlPath}");

        var xaml = File.ReadAllText(mainWindowXamlPath);
        Assert.Matches("behaviors:DragDropReorder\\.IsEnabled\\s*=", xaml);
        Assert.Matches("behaviors:DragDropReorder\\.ReorderCommand\\s*=", xaml);
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KlevaDeploy.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("KlevaDeploy.sln not found walking up from test output directory.");
    }
}

public sealed class CreatePresetReorderTests
{
    [Fact]
    public void ReorderSelectedProcess_MovesSourceBeforeTargetAndRenumbersOrder()
    {
        var processes = new[]
        {
            new DeploymentProcess { Id = "p1", Name = "P1", Description = "D1" },
            new DeploymentProcess { Id = "p2", Name = "P2", Description = "D2" },
            new DeploymentProcess { Id = "p3", Name = "P3", Description = "D3" },
        };

        var vm = new CreatePresetViewModel();
        vm.Initialize(processes);

        vm.ActivateProcessCommand.Execute(vm.AvailableProcesses[0]);
        vm.ActivateProcessCommand.Execute(vm.AvailableProcesses[0]);
        vm.ActivateProcessCommand.Execute(vm.AvailableProcesses[0]);

        var p1 = vm.SelectedProcesses.Single(p => p.Process.Id == "p1");
        var p2 = vm.SelectedProcesses.Single(p => p.Process.Id == "p2");
        var p3 = vm.SelectedProcesses.Single(p => p.Process.Id == "p3");

        vm.ReorderSelectedProcessCommand.Execute(new ProcessReorderRequest(p3, p1, false));

        var ordered = vm.SelectedProcesses.OrderBy(p => p.Order).ToList();

        Assert.Equal(new[] { p3, p1, p2 }, ordered);
        Assert.Equal(new[] { 10, 20, 30 }, ordered.Select(p => p.Order).ToArray());
    }

    [Fact]
    public void ActivateProcess_DoesNotCreateDuplicatesAndUpdatesFilteredSelected()
    {
        var processes = new[]
        {
            new DeploymentProcess { Id = "p1", Name = "P1", Description = "D1" },
            new DeploymentProcess { Id = "p2", Name = "P2", Description = "D2" },
        };

        var vm = new CreatePresetViewModel();
        vm.Initialize(processes);

        var item = vm.AvailableProcesses.Single(p => p.Process.Id == "p1");
        vm.ActivateProcessCommand.Execute(item);
        vm.ActivateProcessCommand.Execute(item);

        Assert.Single(vm.SelectedProcesses);
        Assert.Single(vm.FilteredSelectedProcesses);
        Assert.Equal("p1", vm.SelectedProcesses[0].Process.Id);
    }
}

public sealed class LayoutTests
{
    [Fact]
    public void CreateProcessDialog_ShouldNotOverflowViewport()
    {
        RunOnStaThread(() =>
        {
            EnsureWpfResourcesLoaded();

            var vm = new CreateProcessViewModel();
            var window = new CreateProcessDialog(vm)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                Top = -10000,
                Left = -10000
            };

            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
            window.UpdateLayout();

            var scrollViewer = window.LayoutContentScrollViewer;
            var overflow = scrollViewer.ExtentHeight - scrollViewer.ViewportHeight;

            Assert.True(overflow <= 0.5, $"Vertical overflow too large: {overflow:0.###} DIPs");

            window.Close();
        });
    }

    private static void EnsureWpfResourcesLoaded()
    {
        if (Application.Current is null)
        {
            _ = new Application();
        }

        var resources = Application.Current!.Resources;

        if (resources.Contains("Win11TextSubtitle"))
        {
            return;
        }

        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/KlevaDeploy;component/Themes/Dark.xaml", UriKind.Absolute) });
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/KlevaDeploy;component/Themes/Win11Styles.xaml", UriKind.Absolute) });
        resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/KlevaDeploy;component/Themes/Icons.xaml", UriKind.Absolute) });

        resources["BoolToVisibility"] = new BooleanToVisibilityConverter();
        resources["InverseBoolConverter"] = new InverseBoolConverter();
        resources["InverseBoolToVisibility"] = new InverseBoolToVisibilityConverter();
        resources["StringToVisibilityConverter"] = new StringToVisibilityConverter();
        resources["NullToVisibilityConverter"] = new NullToVisibilityConverter();
        resources["BusyToTextConverter"] = new BusyToTextConverter();
        resources["OrderToDisplayConverter"] = new OrderToDisplayConverter();
    }

    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }
}
