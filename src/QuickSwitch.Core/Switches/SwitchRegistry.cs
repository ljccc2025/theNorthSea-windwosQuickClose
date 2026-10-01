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

        foreach (var item in items)
            Validate(item.Descriptor);

        All = items;
    }

    /// 规格 §8 的自检：加开关时写漏一处，编译期发现不了，但这里能挡住。
    private static void Validate(SwitchDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Id))
            throw new ArgumentException("开关 Id 不能为空。", nameof(descriptor));

        if (string.IsNullOrWhiteSpace(descriptor.Title))
            throw new ArgumentException($"开关 {descriptor.Id} 的标题不能为空。", nameof(descriptor));

        if (string.IsNullOrWhiteSpace(descriptor.Group))
            throw new ArgumentException($"开关 {descriptor.Id} 的分组不能为空。", nameof(descriptor));

        // 破坏性开关必须自带确认文案：默认兜底文案太含糊，用户看不出代价。
        if (descriptor.IsDestructive && string.IsNullOrWhiteSpace(descriptor.ConfirmText))
            throw new ArgumentException($"破坏性开关 {descriptor.Id} 必须给出 ConfirmText。", nameof(descriptor));
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
            new DefenderRealtimeSwitch(powerShell),
            new UacSwitch(registry),
            .. WindowsFeatureCatalog.All.Select(spec => (ISwitch)new WindowsFeatureSwitch(powerShell, spec)),
        ]);
    }
}
