using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class ClipboardHistorySwitchTests
{
    private readonly FakeRegistryStore _store = new();
    private readonly FakeSettingsNotifier _notifier = new();

    private ClipboardHistorySwitch CreateSwitch() => new(_store, _notifier);

    [Fact]
    public void Descriptor_MatchesSpec()
    {
        var descriptor = CreateSwitch().Descriptor;

        Assert.Equal("clipboard-history", descriptor.Id);
        Assert.Equal(SwitchGroup.System, descriptor.Group);
        Assert.Equal("剪贴板历史", descriptor.Title);
    }

    [Fact]
    public void RegistryTargets_MatchSpec()
    {
        Assert.Equal(@"Software\Microsoft\Clipboard", ClipboardHistorySwitch.KeyPath);
        Assert.Equal("EnableClipboardHistory", ClipboardHistorySwitch.ValueName);
    }

    [Fact]
    public async Task Read_ValueMissing_IsUnknown()
    {
        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Unknown, result.State);
        Assert.Contains("EnableClipboardHistory", result.Detail!);
    }

    [Fact]
    public async Task Read_Zero_IsOff()
    {
        _store.SeedDword(ClipboardHistorySwitch.KeyPath, ClipboardHistorySwitch.ValueName, 0);

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Off, result.State);
    }

    [Fact]
    public async Task Read_NonZero_IsOn()
    {
        _store.SeedDword(ClipboardHistorySwitch.KeyPath, ClipboardHistorySwitch.ValueName, 1);

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, result.State);
        Assert.Contains("Win+V", result.Detail!);
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
    public async Task Apply_On_WritesDwordOneAndBroadcasts()
    {
        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        var write = Assert.Single(_store.Writes);
        Assert.Equal(ClipboardHistorySwitch.KeyPath, write.KeyPath);
        Assert.Equal(ClipboardHistorySwitch.ValueName, write.ValueName);
        Assert.Equal(1, write.Value);
        Assert.Equal(1, _notifier.ClipboardNotifications);
        Assert.Equal(0, _notifier.InternetSettingsNotifications);
    }

    [Fact]
    public async Task Apply_Off_WritesDwordZero()
    {
        var result = await CreateSwitch().ApplyAsync(SwitchState.Off, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, Assert.Single(_store.Writes).Value);
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
    public async Task Apply_WriteThrows_FailsAndSkipsBroadcast()
    {
        _store.WriteFailure = new UnauthorizedAccessException("拒绝访问");

        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("拒绝访问", result.Error!);
        Assert.Empty(_store.Writes);
        Assert.Equal(0, _notifier.ClipboardNotifications);
    }

    [Fact]
    public async Task Apply_NotifierThrows_StillSucceeds()
    {
        _notifier.Failure = new InvalidOperationException("广播炸了");

        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, Assert.Single(_store.Writes).Value);
    }
}
