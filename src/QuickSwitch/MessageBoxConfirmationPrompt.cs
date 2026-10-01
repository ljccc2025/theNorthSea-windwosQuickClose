using System.Windows;
using QuickSwitch.Core.ViewModels;

namespace QuickSwitch;

/// 破坏性开关的确认弹窗。取消 = 什么都不做，卡片留在原状态。
public sealed class MessageBoxConfirmationPrompt : IConfirmationPrompt
{
    public bool Confirm(string message) =>
        System.Windows.MessageBox.Show(
            message, "Windows 快捷开关", System.Windows.MessageBoxButton.OKCancel, System.Windows.MessageBoxImage.Warning)
            == System.Windows.MessageBoxResult.OK;
}
