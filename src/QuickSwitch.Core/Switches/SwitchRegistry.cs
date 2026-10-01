using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class SwitchRegistry
{
    public SwitchRegistry(IEnumerable<ISwitch> switches)
    {
        var items = switches.ToArray();
        var duplicate = items
            .GroupBy(item => item.Descriptor.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
            throw new ArgumentException($"开关 Id 重复：{duplicate.Key}", nameof(switches));

        All = items;
    }

    public IReadOnlyList<ISwitch> All { get; }

    /// 显式 new，不用反射：加开关就是加一行，编译期就能发现名字写错。
    /// 守卫命中时只封锁用户级开关（HKCU）——防火墙与电源类开关走 HKLM，不受影响。
    public static SwitchRegistry CreateDefault(
        PowerShellRunner powerShell,
        IRegistryStore registry,
        ISettingsNotifier notifier,
        IPowerCfg powerCfg,
        SessionOwnerState sessionOwner)
    {
        ISwitch GuardUserLevel(ISwitch item) =>
            sessionOwner.IsForeignAdmin
                ? new BlockedSwitch(item, sessionOwner.Detail ?? SessionOwnerGuard.ForeignAdminDetail)
                : item;

        return new(
        [
            new FirewallSwitch(powerShell),
            GuardUserLevel(new SystemProxySwitch(registry, notifier)),
            new HibernateSwitch(registry, powerCfg),
            new FastStartupSwitch(registry),
            new PowerPlanSwitch(powerCfg),
            GuardUserLevel(new ClipboardHistorySwitch(registry, notifier)),
        ]);
    }
}
