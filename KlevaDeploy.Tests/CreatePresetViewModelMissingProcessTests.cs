using System.Collections.Generic;
using KlevaDeploy.Models;
using KlevaDeploy.ViewModels;
using Xunit;

namespace KlevaDeploy.Tests;

public sealed class CreatePresetViewModelMissingProcessTests
{
    [Fact]
    public void InitializeForEdit_DoesNotThrow_WithMissingAndDuplicateProcessIds()
    {
        var vm = new CreatePresetViewModel(presetIconService: null);
        var preset = new DeploymentPreset
        {
            Id = "pkg",
            Name = "Pacchetto",
            Steps = new List<PresetProcessStep>
            {
                new() { ProcessId = "a", Order = 10, IsRequired = true },
                new() { ProcessId = "missing", Order = 20, IsRequired = false },
                new() { ProcessId = "A", Order = 30, IsRequired = false },
            }
        };

        var processes = new List<DeploymentProcess>
        {
            new() { Id = "a", Name = "Process A", Description = "x", Kind = ProcessKind.PowerShellScript, RelativePath = @"Scripts\a.ps1" }
        };

        vm.InitializeForEdit(preset, processes);

        Assert.Contains(vm.SelectedProcesses, p => p.Process.Id == "a");
        Assert.Contains(vm.MissingProcesses, m => m.ProcessId == "missing");
    }

    [Fact]
    public void InitializeForEdit_DoesNotThrow_WithEmptyProcessIdStep()
    {
        var vm = new CreatePresetViewModel(presetIconService: null);
        var preset = new DeploymentPreset
        {
            Id = "pkg",
            Name = "Pacchetto",
            Steps = new List<PresetProcessStep>
            {
                new() { ProcessId = "", Order = 10, IsRequired = true },
            }
        };

        vm.InitializeForEdit(preset, new List<DeploymentProcess>());

        Assert.NotNull(vm.ValidationError);
    }
}

