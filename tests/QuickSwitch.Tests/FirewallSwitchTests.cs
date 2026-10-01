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

    /// 真机写入用例必须诚实：三档不一致时无法用单个布尔往返；未提权时必须断言"失败被如实报告"，
    /// 而不是靠"写当前同值不触发权限校验"混过成为 no-op。
    [Fact]
    public async Task ApplyAsync_RealMachine_FlipsAndRestoresOrReportsFailureHonestly()
    {
        var firewall = CreateSwitch();
        var before = await firewall.ReadAsync(CancellationToken.None);

        if (before.State is not (SwitchState.On or SwitchState.Off))
            return;

        var target = before.State == SwitchState.On ? SwitchState.Off : SwitchState.On;

        var apply = await firewall.ApplyAsync(target, CancellationToken.None);

        if (!apply.Success)
        {
            Assert.False(string.IsNullOrWhiteSpace(apply.Error), "写入失败必须带回原因");

            var unchanged = await firewall.ReadAsync(CancellationToken.None);
            Assert.Equal(before.State, unchanged.State);
            return;
        }

        try
        {
            var after = await firewall.ReadAsync(CancellationToken.None);
            Assert.Equal(target, after.State);
        }
        finally
        {
            await firewall.ApplyAsync(before.State, CancellationToken.None);
        }
    }
}
