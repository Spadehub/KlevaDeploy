using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DeploymentApp.Models;
using DeploymentApp.ViewModels;

namespace DeploymentApp.Tests;

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
            if (File.Exists(Path.Combine(dir.FullName, "InstallerIT.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("InstallerIT.sln not found walking up from test output directory.");
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
}
