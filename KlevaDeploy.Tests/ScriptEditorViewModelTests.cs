using KlevaDeploy.Models;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Tests;

public sealed class ScriptEditorViewModelTests
{
    [Fact]
    public void SwitchingProcessNodeFromSubProcessEditorLoadsSelectedProcessScript()
    {
        var firstProcess = new DeploymentProcess
        {
            Id = "proc-a",
            Name = "First",
            Kind = ProcessKind.PowerShellScript,
            ScriptContent = "Write-Output 'first'"
        };

        var secondProcess = new DeploymentProcess
        {
            Id = "proc-b",
            Name = "Second",
            Kind = ProcessKind.PowerShellScript,
            ScriptContent = "Write-Output 'second'"
        };

        var parentVm = new CreateProcessViewModel
        {
            ProcessName = "Root",
            SelectedProcessKind = ProcessKind.PowerShellScript,
            ScriptContent = "Write-Output 'root'"
        };
        parentVm.SubProcesses.Add(new CreateProcessViewModel.SubProcessItem { Process = firstProcess });
        parentVm.SubProcesses.Add(new CreateProcessViewModel.SubProcessItem { Process = secondProcess });

        var childVm = parentVm.CreateChildViewModel();
        childVm.InitializeForEdit(firstProcess);

        var editorVm = new ScriptEditorViewModel(childVm);

        Assert.Equal("Write-Output 'first'", editorVm.ScriptText);

        var secondNode = editorVm.ProcessNodes
            .SelectMany(Flatten)
            .First(node => node.ProcessId == "proc-b");

        var switched = editorVm.TrySelectProcessNode(secondNode);

        Assert.True(switched);
        Assert.Equal("Second", editorVm.DocumentName);
        Assert.Equal("Write-Output 'second'", editorVm.ScriptText);
        Assert.Equal("Write-Output 'second'", editorVm.SelectedDocument?.ScriptText);
    }

    [Fact]
    public void SwitchingFileBackedProcessNodeLoadsSelectedProcessFile()
    {
        var storageDir = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(storageDir);

        var firstRelativePath = Path.Combine("editor-tests", $"{Guid.NewGuid():N}-first.ps1");
        var secondRelativePath = Path.Combine("editor-tests", $"{Guid.NewGuid():N}-second.ps1");
        var firstFullPath = Path.Combine(storageDir, firstRelativePath);
        var secondFullPath = Path.Combine(storageDir, secondRelativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(firstFullPath)!);
        File.WriteAllText(firstFullPath, "Write-Output 'file-first'");
        File.WriteAllText(secondFullPath, "Write-Output 'file-second'");

        var firstProcess = new DeploymentProcess
        {
            Id = "file-proc-a",
            Name = "File First",
            Kind = ProcessKind.PowerShellScript,
            RelativePath = firstRelativePath
        };

        var secondProcess = new DeploymentProcess
        {
            Id = "file-proc-b",
            Name = "File Second",
            Kind = ProcessKind.PowerShellScript,
            RelativePath = secondRelativePath
        };

        var parentVm = new CreateProcessViewModel
        {
            ProcessName = "Root",
            SelectedProcessKind = ProcessKind.PowerShellScript
        };
        parentVm.SubProcesses.Add(new CreateProcessViewModel.SubProcessItem { Process = firstProcess });
        parentVm.SubProcesses.Add(new CreateProcessViewModel.SubProcessItem { Process = secondProcess });

        var childVm = parentVm.CreateChildViewModel();
        childVm.InitializeForEdit(firstProcess);

        var editorVm = new ScriptEditorViewModel(childVm);

        Assert.Equal("Write-Output 'file-first'", editorVm.ScriptText);

        var secondNode = editorVm.ProcessNodes
            .SelectMany(Flatten)
            .First(node => node.ProcessId == "file-proc-b");

        var switched = editorVm.TrySelectProcessNode(secondNode);

        Assert.True(switched);
        Assert.Equal("File Second", editorVm.DocumentName);
        Assert.Equal("Write-Output 'file-second'", editorVm.ScriptText);
        Assert.Equal("Write-Output 'file-second'", editorVm.SelectedDocument?.ScriptText);
    }

    private static IEnumerable<ScriptEditorViewModel.EditorProcessNode> Flatten(ScriptEditorViewModel.EditorProcessNode root)
    {
        yield return root;

        foreach (var child in root.Children)
        {
            foreach (var descendant in Flatten(child))
                yield return descendant;
        }
    }
}
