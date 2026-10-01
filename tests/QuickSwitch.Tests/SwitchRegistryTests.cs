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
            new FakePowerCfg(),
            sessionOwner ?? SameAccount);

    [Fact]
    public void CreateDefault_RegistersExpectedSwitches()
    {
        var registry = CreateRegistry();

        Assert.Equal(
            [
                "firewall", "system-proxy", "hibernate", "fast-startup", "power-plan", "clipboard-history",
                "defender-realtime", "uac",
                "windows-feature:hyper-v", "windows-feature:wsl", "windows-feature:vm-platform",
            ],
            registry.All.Select(item => item.Descriptor.Id));
        Assert.Equal(
            [
                SwitchGroup.Security, SwitchGroup.Network, SwitchGroup.Power, SwitchGroup.Power,
                SwitchGroup.Power, SwitchGroup.System, SwitchGroup.Security, SwitchGroup.Security,
                SwitchGroup.System, SwitchGroup.System, SwitchGroup.System,
            ],
            registry.All.Select(item => item.Descriptor.Group));
        Assert.IsType<FirewallSwitch>(registry.All[0]);
        Assert.IsType<SystemProxySwitch>(registry.All[1]);
        Assert.IsType<HibernateSwitch>(registry.All[2]);
        Assert.IsType<FastStartupSwitch>(registry.All[3]);
        Assert.IsType<PowerPlanSwitch>(registry.All[4]);
        Assert.IsType<ClipboardHistorySwitch>(registry.All[5]);
        Assert.IsType<DefenderRealtimeSwitch>(registry.All[6]);
        Assert.IsType<UacSwitch>(registry.All[7]);
        Assert.All(
            registry.All.Skip(8),
            item => Assert.IsType<WindowsFeatureSwitch>(item));
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
        var clipboard = registry.All[5];
        Assert.IsType<BlockedSwitch>(proxy);
        Assert.IsType<BlockedSwitch>(clipboard);
        Assert.Equal(SwitchState.Blocked, (await proxy.ReadAsync(CancellationToken.None)).State);
        Assert.Equal(SwitchState.Blocked, (await clipboard.ReadAsync(CancellationToken.None)).State);

        // 防火墙与电源类开关写在 HKLM，和登录账户归属无关，不能被连带封锁。
        Assert.IsType<FirewallSwitch>(registry.All[0]);
        Assert.IsType<HibernateSwitch>(registry.All[2]);
        Assert.IsType<FastStartupSwitch>(registry.All[3]);
        Assert.IsType<PowerPlanSwitch>(registry.All[4]);

        // Defender 与 UAC 也写 HKLM，归属守卫不该碰它们。
        Assert.IsType<DefenderRealtimeSwitch>(registry.All[6]);
        Assert.IsType<UacSwitch>(registry.All[7]);
    }

    [Fact]
    public async Task CreateDefault_SameAccount_ReadsUserLevelSwitches()
    {
        var store = new FakeRegistryStore();
        store.SeedDword(SystemProxySwitch.KeyPath, SystemProxySwitch.EnableValueName, 0);
        store.SeedDword(ClipboardHistorySwitch.KeyPath, ClipboardHistorySwitch.ValueName, 1);

        var fresh = SwitchRegistry.CreateDefault(
            new PowerShellRunner(new ProcessRunner()), store, new FakeSettingsNotifier(), new FakePowerCfg(), SameAccount);
        var proxy = fresh.All[1];
        var clipboard = fresh.All[5];

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

    [Theory]
    [InlineData("", "标题", SwitchGroup.Security)]
    [InlineData("id", "", SwitchGroup.Security)]
    [InlineData("id", "标题", "")]
    public void Constructor_IncompleteDescriptor_Throws(string id, string title, string group)
    {
        var fake = new FakeSwitch { Descriptor = new SwitchDescriptor(id, group, title, "副标题") };

        Assert.Throws<ArgumentException>(() => new SwitchRegistry([fake]));
    }

    [Fact]
    public void Constructor_DestructiveWithoutConfirmText_Throws()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor(
                "destructive", SwitchGroup.Security, "破坏性开关", "副标题", IsDestructive: true),
        };

        var error = Assert.Throws<ArgumentException>(() => new SwitchRegistry([fake]));

        Assert.Contains("ConfirmText", error.Message);
    }

    [Fact]
    public void Constructor_DestructiveWithConfirmText_IsAccepted()
    {
        var fake = new FakeSwitch
        {
            Descriptor = new SwitchDescriptor(
                "destructive", SwitchGroup.Security, "破坏性开关", "副标题",
                IsDestructive: true, ConfirmText: "确定吗？"),
        };

        var registry = new SwitchRegistry([fake]);

        Assert.Single(registry.All);
    }
}
