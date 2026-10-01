using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class SystemProxySwitchTests
{
    private readonly FakeRegistryStore _store = new();
    private readonly FakeSettingsNotifier _notifier = new();

    private SystemProxySwitch CreateSwitch() => new(_store, _notifier);

    [Fact]
    public void Descriptor_MatchesSpec()
    {
        var descriptor = CreateSwitch().Descriptor;

        Assert.Equal("system-proxy", descriptor.Id);
        Assert.Equal(SwitchGroup.Network, descriptor.Group);
        Assert.Equal("系统代理", descriptor.Title);
    }

    [Fact]
    public void RegistryTargets_MatchSpec()
    {
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", SystemProxySwitch.KeyPath);
        Assert.Equal("ProxyEnable", SystemProxySwitch.EnableValueName);
        Assert.Equal("ProxyServer", SystemProxySwitch.ServerValueName);
    }

    [Fact]
    public async Task Read_ProxyEnableMissing_IsOffBySystemDefault()
    {
        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Off, result.State);
        Assert.Contains("ProxyEnable", result.Detail!);
        Assert.Contains("按系统默认", result.Detail!);
    }

    [Fact]
    public async Task Read_Zero_IsOff()
    {
        _store.SeedDword(SystemProxySwitch.KeyPath, SystemProxySwitch.EnableValueName, 0);

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.Off, result.State);
    }

    [Fact]
    public async Task Read_NonZeroWithServer_IsOnAndShowsServer()
    {
        _store.SeedDword(SystemProxySwitch.KeyPath, SystemProxySwitch.EnableValueName, 1);
        _store.SeedString(SystemProxySwitch.KeyPath, SystemProxySwitch.ServerValueName, "127.0.0.1:7890");

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, result.State);
        Assert.Contains("127.0.0.1:7890", result.Detail!);
    }

    [Fact]
    public async Task Read_NonZeroWithoutServer_IsOnAndSaysServerMissing()
    {
        _store.SeedDword(SystemProxySwitch.KeyPath, SystemProxySwitch.EnableValueName, 1);

        var result = await CreateSwitch().ReadAsync(CancellationToken.None);

        Assert.Equal(SwitchState.On, result.State);
        Assert.Contains("未配置代理服务器地址", result.Detail!);
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
    public async Task Apply_On_WritesDwordOneAndNotifies()
    {
        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        var write = Assert.Single(_store.Writes);
        Assert.Equal(SystemProxySwitch.KeyPath, write.KeyPath);
        Assert.Equal(SystemProxySwitch.EnableValueName, write.ValueName);
        Assert.Equal(1, write.Value);
        Assert.Equal(1, _notifier.InternetSettingsNotifications);
        Assert.Equal(0, _notifier.ClipboardNotifications);
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
    [InlineData(SwitchState.Blocked)]
    [InlineData(SwitchState.PendingRestart)]
    public async Task Apply_UnsupportedTarget_Throws(SwitchState target)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => CreateSwitch().ApplyAsync(target, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_WriteThrows_FailsAndSkipsNotification()
    {
        _store.WriteFailure = new UnauthorizedAccessException("拒绝访问");

        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("拒绝访问", result.Error!);
        Assert.Empty(_store.Writes);
        Assert.Equal(0, _notifier.InternetSettingsNotifications);
    }

    [Fact]
    public async Task Apply_NotifierThrows_StillSucceeds()
    {
        // 注册表已写入生效，广播失败不该让卡片显示"操作失败"。
        _notifier.Failure = new InvalidOperationException("wininet 炸了");

        var result = await CreateSwitch().ApplyAsync(SwitchState.On, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, Assert.Single(_store.Writes).Value);
    }
}
