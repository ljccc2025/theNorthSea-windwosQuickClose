using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class HibernateSwitchTests
{
    private readonly FakeRegistryStore _registry = new();
    private readonly FakePowerCfg _powerCfg = new();
    private readonly HibernateSwitch _switch;

    public HibernateSwitchTests() => _switch = new HibernateSwitch(_registry, _powerCfg);

    private void SeedHibernate(int value) =>
        _registry.SeedDword(HibernateSwitch.KeyPath, HibernateSwitch.ValueName, value, RegistryScope.LocalMachine);

    [Fact]
    public void Descriptor_IsPowerGroupSwitch()
    {
        Assert.Equal("hibernate", _switch.Descriptor.Id);
        Assert.Equal(SwitchGroup.Power, _switch.Descriptor.Group);
        Assert.Equal("休眠", _switch.Descriptor.Title);
        Assert.Contains("快速启动", _switch.Descriptor.Subtitle);
        Assert.Equal(@"SYSTEM\CurrentControlSet\Control\Power", HibernateSwitch.KeyPath);
        Assert.Equal("HibernateEnabled", HibernateSwitch.ValueName);
    }

    [Fact]
    public async Task ReadAsync_MissingValue_IsUnknown()
    {
        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, read.State);
        Assert.Contains("HibernateEnabled", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_Zero_IsOff()
    {
        SeedHibernate(0);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Off, read.State);
        Assert.Equal("休眠已关闭，快速启动同时失效", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_NonZero_IsOn()
    {
        SeedHibernate(1);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Contains("hiberfil.sys", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_RegistryFailure_IsUnknownWithMessage()
    {
        _registry.ReadFailure = new UnauthorizedAccessException("拒绝访问");

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, read.State);
        Assert.Contains("拒绝访问", read.Detail);
    }

    [Fact]
    public async Task ApplyAsync_On_RunsPowerCfgHibernateOn()
    {
        SeedHibernate(0);

        var result = await _switch.ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        var call = Assert.Single(_powerCfg.Calls);
        Assert.Equal(["/hibernate", "on"], call);
    }

    [Fact]
    public async Task ApplyAsync_Off_RunsPowerCfgHibernateOff()
    {
        SeedHibernate(1);

        var result = await _switch.ApplyAsync(SwitchState.Off, CancellationToken.None);

        Assert.True(result.Success);
        var call = Assert.Single(_powerCfg.Calls);
        Assert.Equal(["/hibernate", "off"], call);
    }

    [Fact]
    public async Task ApplyAsync_NonZeroExit_IsFailureWithFirstErrorLine()
    {
        _powerCfg.Fallback = new ProcessResult(1, string.Empty, "无法禁用休眠。\r\n原因：权限不足");

        var result = await _switch.ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("无法禁用休眠。", result.Error);
    }

    [Theory]
    [InlineData(SwitchState.Unknown)]
    [InlineData(SwitchState.Mixed)]
    [InlineData(SwitchState.Blocked)]
    [InlineData(SwitchState.PendingRestart)]
    public async Task ApplyAsync_InvalidTarget_Throws(SwitchState target)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _switch.ApplyAsync(target, CancellationToken.None));
        Assert.Empty(_powerCfg.Calls);
    }
}
