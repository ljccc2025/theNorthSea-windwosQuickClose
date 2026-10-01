using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class WindowsFeatureSwitchTests
{
    private static readonly WindowsFeatureSpec Spec =
        new("Microsoft-Hyper-V-All", "windows-feature:hyper-v", "Hyper-V", "虚拟机平台");

    private static WindowsFeatureSwitch CreateSwitch() =>
        new(new PowerShellRunner(new ProcessRunner()), Spec);

    [Fact]
    public void Descriptor_ComesFromSpec_AndRequiresRestart()
    {
        var descriptor = CreateSwitch().Descriptor;

        Assert.Equal("windows-feature:hyper-v", descriptor.Id);
        Assert.Equal(SwitchGroup.System, descriptor.Group);
        Assert.Equal("Hyper-V", descriptor.Title);
        Assert.True(descriptor.RequiresRestart);
        Assert.True(descriptor.IsDestructive);
        Assert.Contains("Hyper-V", descriptor.ConfirmText!);
    }

    [Fact]
    public void Catalog_HoldsThreeDistinctFeatures()
    {
        var specs = WindowsFeatureCatalog.All;

        Assert.Equal(3, specs.Count);
        Assert.Equal(3, specs.Select(spec => spec.FeatureName).Distinct().Count());
        Assert.Equal(3, specs.Select(spec => spec.Id).Distinct().Count());
        Assert.Contains(specs, spec => spec.FeatureName == "Microsoft-Hyper-V-All");
        Assert.Contains(specs, spec => spec.FeatureName == "Microsoft-Windows-Subsystem-Linux");
        Assert.Contains(specs, spec => spec.FeatureName == "VirtualMachinePlatform");
    }

    [Fact]
    public void BuildReadScript_QueriesOnlineState()
    {
        var script = WindowsFeatureSwitch.BuildReadScript("Microsoft-Hyper-V-All");

        Assert.Contains("Get-WindowsOptionalFeature -Online -FeatureName 'Microsoft-Hyper-V-All'", script);
        Assert.Contains(".State", script);
    }

    [Fact]
    public void BuildApplyScript_UsesNoRestart()
    {
        var enable = WindowsFeatureSwitch.BuildApplyScript("VirtualMachinePlatform", SwitchState.On);
        var disable = WindowsFeatureSwitch.BuildApplyScript("VirtualMachinePlatform", SwitchState.Off);

        Assert.Contains("Enable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform'", enable);
        Assert.Contains("Disable-WindowsOptionalFeature -Online -FeatureName 'VirtualMachinePlatform'", disable);
        Assert.Contains("-NoRestart", enable);
        Assert.Contains("-NoRestart", disable);
    }

    [Theory]
    [InlineData(SwitchState.Unknown)]
    [InlineData(SwitchState.Mixed)]
    [InlineData(SwitchState.PendingRestart)]
    public void BuildApplyScript_UnsupportedTarget_Throws(SwitchState target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WindowsFeatureSwitch.BuildApplyScript("VirtualMachinePlatform", target));
    }

    [Theory]
    [InlineData(@"Hyper-V'; Remove-Item -Recurse C:\temp")]
    [InlineData("")]
    [InlineData("feat ure")]
    public void EnsureSafeFeatureName_RejectsInjection(string featureName)
    {
        Assert.Throws<ArgumentException>(() => WindowsFeatureSwitch.EnsureSafeFeatureName(featureName));
    }

    [Fact]
    public void OperationTimeout_IsTenMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), WindowsFeatureSwitch.OperationTimeout);
    }

    /// 真机读：未提权时 Get-WindowsOptionalFeature 会失败，但必须收成"未知 + 原因"而不是异常。
    [Fact]
    public async Task Read_RealMachineWithoutElevation_IsUnknownNotCrash()
    {
        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Contains(
            result.State,
            new[] { SwitchState.On, SwitchState.Off, SwitchState.PendingRestart, SwitchState.Unknown });
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }
}

