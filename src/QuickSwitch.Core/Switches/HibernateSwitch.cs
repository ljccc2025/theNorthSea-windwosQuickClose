using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class HibernateSwitch : ISwitch
{
    public const string KeyPath = @"SYSTEM\CurrentControlSet\Control\Power";
    public const string ValueName = "HibernateEnabled";

    private readonly IRegistryStore _registry;
    private readonly IPowerCfg _powerCfg;

    public HibernateSwitch(IRegistryStore registry, IPowerCfg powerCfg)
    {
        _registry = registry;
        _powerCfg = powerCfg;
    }

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "hibernate",
        Group: SwitchGroup.Power,
        Title: "休眠",
        Subtitle: "关闭后删除 hiberfil.sys，快速启动同时失效");

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _registry.ReadDword(RegistryScope.LocalMachine, KeyPath, ValueName);
            return Task.FromResult(enabled switch
            {
                null => new SwitchReadResult(
                    SwitchState.Unknown, $"注册表里没有 {ValueName}，无法判定休眠状态"),
                0 => new SwitchReadResult(SwitchState.Off, "休眠已关闭，快速启动同时失效"),
                _ => new SwitchReadResult(SwitchState.On, "休眠已开启，hiberfil.sys 占用磁盘"),
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SwitchReadResult(SwitchState.Unknown, $"读取休眠状态失败：{ex.Message}"));
        }
    }

    public async Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var flag = target switch
        {
            SwitchState.On => "on",
            SwitchState.Off => "off",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "休眠只接受 On 或 Off。"),
        };

        // 必须走 powercfg：直接改注册表不会删/建 hiberfil.sys。
        var result = await _powerCfg.RunAsync(["/hibernate", flag], cancellationToken).ConfigureAwait(false);

        return result.Succeeded
            ? SwitchApplyResult.Ok()
            : SwitchApplyResult.Fail(ErrorText.FirstLine(result.StandardError));
    }
}
