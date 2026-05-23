using KlevaDeploy.Models;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Tests;

public sealed class CreateProcessDialogTests
{
    [Fact]
    public void CancelCommand_SetsDialogResultFalse()
    {
        var vm = new CreateProcessViewModel();
        Assert.Null(vm.DialogResult);

        vm.CancelCommand.Execute(null);

        Assert.False(vm.DialogResult);
        Assert.Null(vm.CreatedProcess);
    }

    [Fact]
    public void SaveCommand_SetsDialogResultTrue_ForScriptContent()
    {
        var vm = new CreateProcessViewModel
        {
            ProcessName = "Test Proc",
            Description = "Desc"
        };
        vm.SelectedProcessKind = ProcessKind.PowerShellScript;
        vm.ScriptContent = "Write-Host 'Hello'";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.DialogResult);
        Assert.NotNull(vm.CreatedProcess);
        Assert.Equal("Test Proc", vm.CreatedProcess!.Name);
        Assert.Equal(ProcessKind.PowerShellScript, vm.CreatedProcess.Kind);
    }
}

