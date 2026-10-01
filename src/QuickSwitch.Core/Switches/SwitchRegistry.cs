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
    public static SwitchRegistry CreateDefault(PowerShellRunner powerShell) =>
        new([new FirewallSwitch(powerShell)]);
}
