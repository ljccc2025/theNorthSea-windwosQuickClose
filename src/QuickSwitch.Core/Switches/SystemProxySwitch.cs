using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class SystemProxySwitch : ISwitch
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    public const string EnableValueName = "ProxyEnable";
    public const string ServerValueName = "ProxyServer";

    private readonly IRegistryStore _registry;
    private readonly ISettingsNotifier _notifier;

    public SystemProxySwitch(IRegistryStore registry, ISettingsNotifier notifier)
    {
        _registry = registry;
        _notifier = notifier;
    }

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "system-proxy",
        Group: SwitchGroup.Network,
        Title: "系统代理",
        Subtitle: "当前用户的 WinINET 代理（ProxyEnable）");

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _registry.ReadDword(KeyPath, EnableValueName);
            if (enabled is null)
                return Task.FromResult(new SwitchReadResult(
                    SwitchState.Unknown, $"注册表里没有 {EnableValueName}，无法判定系统代理状态"));

            if (enabled == 0)
                return Task.FromResult(new SwitchReadResult(SwitchState.Off, "系统代理已关闭"));

            var server = _registry.ReadString(KeyPath, ServerValueName);
            return Task.FromResult(new SwitchReadResult(
                SwitchState.On,
                string.IsNullOrWhiteSpace(server) ? "系统代理已开启，但未配置代理服务器地址" : $"代理服务器：{server}"));
        }
        catch (Exception ex)
        {
            // 读失败不是崩溃的场合：原样回显系统报错，卡片停在 Unknown。
            return Task.FromResult(new SwitchReadResult(SwitchState.Unknown, $"读取系统代理失败：{ex.Message}"));
        }
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var value = target switch
        {
            SwitchState.On => 1,
            SwitchState.Off => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "系统代理只接受 On 或 Off。"),
        };

        try
        {
            _registry.WriteDword(KeyPath, EnableValueName, value);
        }
        catch (Exception ex)
        {
            return Task.FromResult(SwitchApplyResult.Fail($"写入系统代理失败：{ex.Message}"));
        }

        NotifyQuietly(_notifier.NotifyInternetSettingsChanged);
        return Task.FromResult(SwitchApplyResult.Ok());
    }

    private static void NotifyQuietly(Action notify)
    {
        try
        {
            notify();
        }
        catch (Exception)
        {
            // 注册表已经写入生效，广播失败不该让卡片报"操作失败"。
        }
    }
}
