using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class UacSwitch : ISwitch
{
    public const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    public const string ValueName = "EnableLUA";

    private readonly IRegistryStore _registry;

    public UacSwitch(IRegistryStore registry) => _registry = registry;

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "uac",
        Group: SwitchGroup.Security,
        Title: "用户账户控制 (UAC)",
        Subtitle: "关闭后程序静默提权；改回同样需要重启",
        RequiresRestart: true,
        IsDestructive: true,
        ConfirmText: "关闭 UAC 后需要重启才彻底生效，改回时还要再重启一次；期间 Microsoft Store 应用可能打不开。确定要关闭吗？");

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _registry.ReadDword(RegistryScope.LocalMachine, KeyPath, ValueName);
            return Task.FromResult(enabled switch
            {
                null => new SwitchReadResult(
                    SwitchState.Unknown, $"注册表里没有 {ValueName}，无法判定 UAC 状态"),
                0 => new SwitchReadResult(SwitchState.Off, "UAC 已关闭，重启后才完全生效"),
                _ => new SwitchReadResult(SwitchState.On, "UAC 已开启"),
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SwitchReadResult(SwitchState.Unknown, $"读取 UAC 状态失败：{ex.Message}"));
        }
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var value = target switch
        {
            SwitchState.On => 1,
            SwitchState.Off => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "UAC 只接受 On 或 Off。"),
        };

        try
        {
            _registry.WriteDword(RegistryScope.LocalMachine, KeyPath, ValueName, value);
            return Task.FromResult(SwitchApplyResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(SwitchApplyResult.Fail($"写入 UAC 设置失败：{ex.Message}"));
        }
    }
}

