using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;
using QuickSwitch.Tests.Fakes;

namespace QuickSwitch.Tests;

public class SwitchRegistryTests
{
    private static readonly SessionOwnerState SameAccount = SessionOwnerState.SameAccount;

    private static readonly SessionOwnerState ForeignAdmin = new(true, SessionOwnerGuard.ForeignAdminDetail);

    private static SwitchRegistry CreateRegistry(SessionOwnerState? sessionOwner = null) =>
        SwitchRegistry.CreateDefault(
            new PowerShellRunner(new ProcessRunner()),
            new FakeRegistryStore(),
            new FakeSettingsNotifier(),
            sessionOwner ?? SameAccount);

    [Fact]
    public void CreateDefault_RegistersExpectedSwitches()
    {
        var registry = CreateRegistry();

        Assert.Equal(["firewall", "system-proxy", "clipboard-history"], registry.All.Select(item => item.Descriptor.Id));
        Assert.Equal(
            [SwitchGroup.Security, SwitchGroup.Network, SwitchGroup.System],
            registry.All.Select(item => item.Descriptor.Group));
        Assert.IsType<FirewallSwitch>(registry.All[0]);
        Assert.IsType<SystemProxySwitch>(registry.All[1]);
        Assert.IsType<ClipboardHistorySwitch>(registry.All[2]);
    }

    [Fact]
    public void CreateDefault_EveryDescriptorIsFullyPopulated()
    {
        foreach (var item in CreateRegistry().All)
        {
            var descriptor = item.Descriptor;
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Id));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Group));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Title));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Subtitle));
        }
    }

    [Fact]
    public void CreateDefault_NoDuplicateIds()
    {
        var ids = CreateRegistry().All.Select(item => item.Descriptor.Id).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task CreateDefault_ForeignAdmin_BlocksUserLevelSwitchesOnly()
    {
        var registry = CreateRegistry(ForeignAdmin);

        var proxy = registry.All[1];
        var clipboard = registry.All[2];
        Assert.IsType<BlockedSwitch>(proxy);
        Assert.IsType<BlockedSwitch>(clipboard);
        Assert.Equal(SwitchState.Blocked, (await proxy.ReadAsync(CancellationToken.None)).State);
        Assert.Equal(SwitchState.Blocked, (await clipboard.ReadAsync(CancellationToken.None)).State);

        // 防火墙写在 HKLM，和登录账户归属无关，不能被连带封锁。
        Assert.IsType<FirewallSwitch>(registry.All[0]);
    }

    [Fact]
    public async Task CreateDefault_SameAccount_ReadsUserLevelSwitches()
    {
        var registry = CreateRegistry();
        var store = new FakeRegistryStore();
        store.SeedDword(SystemProxySwitch.KeyPath, SystemProxySwitch.EnableValueName, 0);
        store.SeedDword(ClipboardHistorySwitch.KeyPath, ClipboardHistorySwitch.ValueName, 1);

        var fresh = SwitchRegistry.CreateDefault(
            new PowerShellRunner(new ProcessRunner()), store, new FakeSettingsNotifier(), SameAccount);
        var proxy = fresh.All[1];
        var clipboard = fresh.All[2];

        Assert.IsType<SystemProxySwitch>(proxy);
        Assert.IsType<ClipboardHistorySwitch>(clipboard);
        Assert.Equal(SwitchState.Off, (await proxy.ReadAsync(CancellationToken.None)).State);
        Assert.Equal(SwitchState.On, (await clipboard.ReadAsync(CancellationToken.None)).State);
    }

    [Fact]
    public void Constructor_DuplicateIds_Throws()
    {
        var powerShell = new PowerShellRunner(new ProcessRunner());

        Assert.Throws<ArgumentException>(() => new SwitchRegistry(
            [new FirewallSwitch(powerShell), new FirewallSwitch(powerShell)]));
    }
}
