namespace QuickSwitch.Core.Switches;

/// 开关的静态元数据。后三项是 M4 才用上的"破坏性 / 需重启"标记，默认值让既有开关不用改。
public sealed record SwitchDescriptor(
    string Id,
    string Group,
    string Title,
    string Subtitle,
    bool RequiresRestart = false,
    bool IsDestructive = false,
    string? ConfirmText = null);

