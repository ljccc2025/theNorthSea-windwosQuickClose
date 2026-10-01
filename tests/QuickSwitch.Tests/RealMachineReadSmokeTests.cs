using QuickSwitch.Core.Infrastructure;
using QuickSwitch.Core.Switches;

namespace QuickSwitch.Tests;

/// 真机冒烟：把整张注册表在真实系统上全部读一遍。
/// 只读，不改系统；未提权时部分卡片必然读不到，但**绝不能抛异常**。
public class RealMachineReadSmokeTests
{
    private static SwitchRegistry CreateRegistry() =>
        SwitchRegistry.CreateDefault(
            new PowerShellRunner(new ProcessRunner()),
            new WindowsRegistryStore(),
            new Win32SettingsNotifier(),
            new PowerCfg(new ProcessRunner()),
            new SessionOwnerGuard(new WindowsUserSidSource()).Evaluate());

    [Fact]
    public async Task ReadAll_OnRealMachine_NeverThrowsAndAlwaysExplains()
    {
        var registry = CreateRegistry();

        Assert.Equal(11, registry.All.Count);

        foreach (var item in registry.All)
        {
            var result = await item.ReadAsync(CancellationToken.None);

            Assert.False(string.IsNullOrWhiteSpace(result.Detail), $"{item.Descriptor.Id} 没有给出任何说明");
            Assert.Contains(
                result.State,
                new[]
                {
                    SwitchState.On, SwitchState.Off, SwitchState.Mixed,
                    SwitchState.Blocked, SwitchState.PendingRestart, SwitchState.Unknown,
                });
        }
    }
}
