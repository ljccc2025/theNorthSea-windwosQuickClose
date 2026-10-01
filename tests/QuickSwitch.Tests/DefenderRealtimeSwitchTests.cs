using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class DefenderRealtimeSwitchTests
{
    private static DefenderRealtimeSwitch CreateSwitch() =>
        new(new PowerShellRunner(new ProcessRunner()));

    [Fact]
    public void Descriptor_MatchesSpec()
    {
        var descriptor = CreateSwitch().Descriptor;

        Assert.Equal("defender-realtime", descriptor.Id);
        Assert.Equal(SwitchGroup.Security, descriptor.Group);
        Assert.Equal("实时防护", descriptor.Title);
        Assert.False(descriptor.RequiresRestart);
    }

    [Fact]
    public void BuildApplyScript_MapsTargetToDisableFlag()
    {
        Assert.Contains(
            "Set-MpPreference -DisableRealtimeMonitoring $true",
            DefenderRealtimeSwitch.BuildApplyScript(SwitchState.Off));
        Assert.Contains(
            "Set-MpPreference -DisableRealtimeMonitoring $false",
            DefenderRealtimeSwitch.BuildApplyScript(SwitchState.On));
    }

    [Theory]
    [InlineData(SwitchState.Unknown)]
    [InlineData(SwitchState.Mixed)]
    [InlineData(SwitchState.PendingRestart)]
    public void BuildApplyScript_UnsupportedTarget_Throws(SwitchState target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DefenderRealtimeSwitch.BuildApplyScript(target));
    }

    /// 真机读：Defender 可能被第三方杀软取代或篡改防护拦着，但绝不能抛异常或崩。
    [Fact]
    public async Task Read_RealMachine_ReturnsKnownStateOrUnknownWithoutThrowing()
    {
        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Contains(
            result.State,
            new[] { SwitchState.On, SwitchState.Off, SwitchState.Blocked, SwitchState.Unknown });
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }
}

