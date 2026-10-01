using QuickSwitch.Core.Infrastructure;

namespace QuickSwitch.Core.Switches;

public sealed class ClipboardHistorySwitch : ISwitch
{
    public const string KeyPath = @"Software\Microsoft\Clipboard";
    public const string ValueName = "EnableClipboardHistory";

    private readonly IRegistryStore _registry;
    private readonly ISettingsNotifier _notifier;

    public ClipboardHistorySwitch(IRegistryStore registry, ISettingsNotifier notifier)
    {
        _registry = registry;
        _notifier = notifier;
    }

    public SwitchDescriptor Descriptor { get; } = new(
        Id: "clipboard-history",
        Group: SwitchGroup.System,
        Title: "剪贴板历史",
        Subtitle: "按 Win+V 查看历史记录");

    public Task<SwitchReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var enabled = _registry.ReadDword(RegistryScope.CurrentUser, KeyPath, ValueName);
            return Task.FromResult(enabled switch
            {
                null => new SwitchReadResult(
                    SwitchState.Unknown, $"注册表里没有 {ValueName}，无法判定剪贴板历史状态"),
                0 => new SwitchReadResult(SwitchState.Off, "剪贴板历史已关闭"),
                _ => new SwitchReadResult(SwitchState.On, "剪贴板历史已开启，按 Win+V 查看"),
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SwitchReadResult(SwitchState.Unknown, $"读取剪贴板历史失败：{ex.Message}"));
        }
    }

    public Task<SwitchApplyResult> ApplyAsync(SwitchState target, CancellationToken cancellationToken)
    {
        var value = target switch
        {
            SwitchState.On => 1,
            SwitchState.Off => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "剪贴板历史只接受 On 或 Off。"),
        };

        try
        {
            _registry.WriteDword(RegistryScope.CurrentUser, KeyPath, ValueName, value);
        }
        catch (Exception ex)
        {
            return Task.FromResult(SwitchApplyResult.Fail($"写入剪贴板历史失败：{ex.Message}"));
        }

        try
        {
            _notifier.NotifyClipboardSettingChanged();
        }
        catch (Exception)
        {
            // 同上：写入已生效，通知失败不改判定。
        }

        return Task.FromResult(SwitchApplyResult.Ok());
    }
}
