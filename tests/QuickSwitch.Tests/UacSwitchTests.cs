using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class UacSwitchTests
{
    private readonly FakeRegistryStore _store = new();

    private UacSwitch CreateSwitch() => new(_store);

    [Fact]
    public void Descriptor_MatchesSpec()
    {
        var descriptor = CreateSwitch().Descriptor;

        Assert.Equal("uac", descriptor.Id);
        Assert.Equal(SwitchGroup.Security, descriptor.Group);
        Assert.Equal("用户账户控制 (UAC)", descriptor.Title);
        Assert.True(descriptor.RequiresRestart);
        Assert.True(descriptor.IsDestructive);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.ConfirmText));
        Assert.Contains("重启", descriptor.ConfirmText!);
    }

    [Fact]
    public void RegistryTargets_MatchSpec()
    {
        Assert.Equal(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
            UacSwitch.KeyPath);
        Assert.Equal("EnableLUA", UacSwitch.ValueName);
    }

    [Fact]
    public async Task Read_ValueMissing_IsUnknown()
    {
        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("EnableLUA", result.Detail!);
    }

    [Fact]
    public async Task Read_Zero_IsOff()
    {
        _store.SeedDword(
            UacSwitch.KeyPath, UacSwitch.ValueName, 0, RegistryScope.LocalMachine);

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Off, result.State);
    }

    [Fact]
    public async Task Read_One_IsOn()
    {
        _store.SeedDword(
            UacSwitch.KeyPath, UacSwitch.ValueName, 1, RegistryScope.LocalMachine);

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, result.State);
    }

    [Fact]
    public async Task Read_StoreThrows_IsUnknownNotCrash()
    {
        _store.ReadFailure = new UnauthorizedAccessException("拒绝访问");

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("拒绝访问", result.Detail!);
    }

    [Fact]
    public async Task Apply_Off_WritesLocalMachineDwordZero()
    {
        var result = await CreateSwitch().ApplyAsync(SwitchState.Off, CancellationToken.None);

        Assert.True(result.Success);
        var write = Assert.Single(_store.Writes);
        Assert.Equal(RegistryScope.LocalMachine, write.Scope);
        Assert.Equal(UacSwitch.KeyPath, write.KeyPath);
        Assert.Equal(UacSwitch.ValueName, write.ValueName);
        Assert.Equal(0, write.Value);
    }

    [Fact]
    public async Task Apply_On_WritesDwordOne()
    {
        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, Assert.Single(_store.Writes).Value);
    }

    [Theory]
    [InlineData(SwitchState.Unknown)]
    [InlineData(SwitchState.Mixed)]
    [InlineData(SwitchState.PendingRestart)]
    public async Task Apply_UnsupportedTarget_Throws(SwitchState target)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateSwitch().ApplyAsync(target, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_WriteThrows_FailsWithReason()
    {
        _store.WriteFailure = new UnauthorizedAccessException("拒绝访问");

        var result = await CreateSwitch().ApplyAsync(SwitchState.Off, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("拒绝访问", result.Error!);
    }
}

