using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

public class FirewallSwitchTests
{
    private static FirewallSwitch CreateSwitch() => new(new PowerShellRunner(new ProcessRunner()));

    [Fact]
    public void BuildApplyScript_Enable_UsesBareStringToken()
    {
        var script = FirewallSwitch.BuildApplyScript(SwitchState.On);

        Assert.Equal("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True", script);
        Assert.DoesNotContain("$true", script);
        Assert.DoesNotContain("$false", script);
    }

    [Fact]
    public void BuildApplyScript_Disable_UsesBareStringToken()
    {
        var script = FirewallSwitch.BuildApplyScript(SwitchState.Off);

        Assert.Equal("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled False", script);
    }

    [Theory]
    [InlineData(SwitchState.Unknown)]
    [InlineData(SwitchState.Mixed)]
    [InlineData(SwitchState.Blocked)]
    [InlineData(SwitchState.PendingRestart)]
    public void BuildApplyScript_NonBinaryTarget_Throws(SwitchState target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FirewallSwitch.BuildApplyScript(target));
    }

    [Fact]
    public void ReadScript_TargetsAllThreeProfiles()
    {
        Assert.Contains("Get-NetFirewallProfile", FirewallSwitch.ReadScript);
        Assert.Contains("Domain,Private,Public", FirewallSwitch.ReadScript);
    }

    [Fact]
    public async Task ReadAsync_AgainstRealMachine_ReturnsKnownState()
    {
        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Contains(result.State, new[] { SwitchState.On, SwitchState.Off, SwitchState.Mixed });
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task ApplyAsync_CurrentState_IsIdempotentAndReadsBack()
    {
        var firewall = CreateSwitch();
        var before = await firewall.ReadAsync(CancellationToken.None);
        Assert.NotEqual(SwitchState.Unknown, before.State);

        var apply = await firewall.ApplyAsync(before.State, CancellationToken.None);

        Assert.True(apply.Success, apply.Error);
        var after = await firewall.ReadAsync(CancellationToken.None);
        Assert.Equal(before.State, after.State);
    }
}
