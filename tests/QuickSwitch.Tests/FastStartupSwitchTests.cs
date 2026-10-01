using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class FastStartupSwitchTests
{
    private readonly FakeRegistryStore _registry = new();
    private readonly FastStartupSwitch _switch;

    public FastStartupSwitchTests() => _switch = new FastStartupSwitch(_registry);

    private void SeedFastStartup(int value) =>
        _registry.SeedDword(FastStartupSwitch.KeyPath, FastStartupSwitch.ValueName, value, RegistryScope.LocalMachine);

    private void SeedHibernate(int value) =>
        _registry.SeedDword(HibernateSwitch.KeyPath, HibernateSwitch.ValueName, value, RegistryScope.LocalMachine);

    [Fact]
    public void Descriptor_IsPowerGroupSwitch()
    {
        Assert.Equal("fast-startup", _switch.Descriptor.Id);
        Assert.Equal(SwitchGroup.Power, _switch.Descriptor.Group);
        Assert.Equal("快速启动", _switch.Descriptor.Title);
        Assert.Equal(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", FastStartupSwitch.KeyPath);
        Assert.Equal("HiberbootEnabled", FastStartupSwitch.ValueName);
    }

    [Fact]
    public async Task ReadAsync_MissingValue_IsUnknown()
    {
        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, read.State);
        Assert.Contains("HiberbootEnabled", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_Zero_IsOff()
    {
        SeedFastStartup(0);
        SeedHibernate(1);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Off, read.State);
        Assert.Equal("快速启动已关闭", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_On_WithHibernateEnabled_IsPlainOn()
    {
        SeedFastStartup(1);
        SeedHibernate(1);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("快速启动已开启", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_On_WithHibernateDisabled_ShowsDependencyHint()
    {
        SeedFastStartup(1);
        SeedHibernate(0);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("快速启动已开启，但休眠已关闭，实际不生效", read.Detail);
    }

    [Fact]
    public async Task ReadAsync_On_WithHibernateValueMissing_IsPlainOn()
    {
        SeedFastStartup(1);

        var read = await _switch.ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, read.State);
        Assert.Equal("快速启动已开启", read.Detail);
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
    public async Task ApplyAsync_On_WritesOneToLocalMachine()
    {
        var result = await _switch.ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        var write = Assert.Single(_registry.Writes);
        Assert.Equal(RegistryScope.LocalMachine, write.Scope);
        Assert.Equal(FastStartupSwitch.KeyPath, write.KeyPath);
        Assert.Equal(FastStartupSwitch.ValueName, write.ValueName);
        Assert.Equal(1, write.Value);
    }

    [Fact]
    public async Task ApplyAsync_Off_WritesZero()
    {
        SeedFastStartup(1);

        var result = await _switch.ApplyAsync(SwitchState.Off, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, Assert.Single(_registry.Writes).Value);
    }

    [Fact]
    public async Task ApplyAsync_WriteFailure_IsFailure()
    {
        _registry.WriteFailure = new UnauthorizedAccessException("拒绝访问");

        var result = await _switch.ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("写入快速启动失败", result.Error);
        Assert.Contains("拒绝访问", result.Error);
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
        Assert.Empty(_registry.Writes);
    }
}
