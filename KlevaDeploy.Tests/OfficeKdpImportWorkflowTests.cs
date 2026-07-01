using KlevaDeploy.Models;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Tests;

public sealed class OfficeKdpImportWorkflowTests
{
    [Fact]
    public void InitializeForEdit_ThenSave_PreservesImportedInstallerIdentityAndCachePath()
    {
        var imported = new DeploymentProcess
        {
            Id = "microsoft-365-apps",
            Name = "Microsoft 365 Apps",
            Description = "Office cache-first workflow",
            Kind = ProcessKind.Installer,
            InstallerSourceMode = InstallerSourceMode.StaticWeb,
            RelativePath = @"Data\installers\microsoft-365-apps\setup.exe",
            DownloadUrl = "https://officecdn.microsoft.com/pr/wsus/setup.exe",
            DownloadUseLatestVersion = true,
            RequiresInternet = true,
            IsUserCreated = true
        };

        var vm = new CreateProcessViewModel();
        vm.InitializeForEdit(imported);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.DialogResult);
        Assert.NotNull(vm.CreatedProcess);
        Assert.Equal("microsoft-365-apps", vm.CreatedProcess!.Id);
        Assert.Equal(@"Data\installers\microsoft-365-apps\setup.exe", vm.CreatedProcess.RelativePath);
    }
}
