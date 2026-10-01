using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace QuickSwitch.Controls;

/// ToggleButton 的自动化缺口：默认 ToggleButtonAutomationPeer.Toggle() 只写 IsChecked、不触发
/// Command，于是讲述人/自动化脚本一按，界面翻过去了、系统真值没动 —— UI 在说谎。
/// 这里把 Toggle() 接到真命令上，状态照旧由卡片写完后的权威回读决定。
public class CommandToggleButton : ToggleButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new CommandToggleButtonAutomationPeer(this);
}

internal sealed class CommandToggleButtonAutomationPeer : ToggleButtonAutomationPeer, IToggleProvider
{
    private readonly CommandToggleButton _owner;

    public CommandToggleButtonAutomationPeer(CommandToggleButton owner) : base(owner) => _owner = owner;

    void IToggleProvider.Toggle()
    {
        // 命令不可执行（状态未知/已封锁/正忙）时什么都不做：IsChecked 是 OneWay 绑定，
        // 自己翻一下只会造出"界面说关了、系统还开着"的假象。
        if (_owner.Command is not null && _owner.Command.CanExecute(_owner.CommandParameter))
            _owner.Command.Execute(_owner.CommandParameter);
    }
}
