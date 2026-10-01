using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class FastStartupSwitch : ISwitch
{
    public const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    public const string ValueName = "HiberbootEnabled";

    private readonly IRegistryStore _registry;

    public FastStartupSwitch(IRegistryStore registry) => _registry = registry;

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "fast-startup",
        Group: SwitchGroup.Power,
        Title: "快速启动",
        Subtitle: "关机后再开机走混合启动，需要休眠可用");

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _registry.ReadDword(RegistryScope.LocalMachine, KeyPath, ValueName);
            if (enabled is null)
                return Task.FromResult(new SwitchReadResult(
                    SwitchState.Unknown, $"注册表里没有 {ValueName}，无法判定快速启动状态"));

            if (enabled == 0)
                return Task.FromResult(new SwitchReadResult(SwitchState.Off, "快速启动已关闭"));

            // 依赖关系只提示不拦：休眠关着时快速启动其实不生效（规格 §6）。
            var hibernate = _registry.ReadDword(
                RegistryScope.LocalMachine, HibernateSwitch.KeyPath, HibernateSwitch.ValueName);

            return Task.FromResult(new SwitchReadResult(
                SwitchState.On,
                hibernate == 0 ? "快速启动已开启，但休眠已关闭，实际不生效" : "快速启动已开启"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SwitchReadResult(SwitchState.Unknown, $"读取快速启动状态失败：{ex.Message}"));
        }
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var value = target switch
        {
            SwitchState.On => 1,
            SwitchState.Off => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "快速启动只接受 On 或 Off。"),
        };

        try
        {
            _registry.WriteDword(RegistryScope.LocalMachine, KeyPath, ValueName, value);
            return Task.FromResult(SwitchApplyResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(SwitchApplyResult.Fail($"写入快速启动失败：{ex.Message}"));
        }
    }
}
