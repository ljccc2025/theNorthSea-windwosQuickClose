namespace QuickSwitch.Core.ViewModels;

/// 破坏性开关（关掉要重启 / 关掉有副作用）写之前过一次确认。
/// Core 不依赖 WPF，弹窗由外壳注入；测试注入一个只会点头的假实现。
public interface IConfirmationPrompt
{
    bool Confirm(string message);
}

