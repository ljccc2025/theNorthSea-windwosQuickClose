namespace QuickSwitch.Core.Infrastructure;

public interface ISettingsNotifier
{
    /// 只写 ProxyEnable 不够：已经跑着的进程不会重读，必须双发
    /// INTERNET_OPTION_SETTINGS_CHANGED 与 INTERNET_OPTION_REFRESH。
    void NotifyInternetSettingsChanged();

    /// 剪贴板历史的生效与否由 explorer 决定，写注册表后广播 WM_SETTINGCHANGE。
    void NotifyClipboardSettingChanged();
}

/// 通知是尽力而为：广播失败不回滚已落盘的写入，所以这里不抛异常。
public sealed class Win32SettingsNotifier : ISettingsNotifier
{
    public void NotifyInternetSettingsChanged()
    {
        NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.InternetOptionSettingsChanged, IntPtr.Zero, 0);
        NativeMethods.InternetSetOption(IntPtr.Zero, NativeMethods.InternetOptionRefresh, IntPtr.Zero, 0);
    }

    public void NotifyClipboardSettingChanged() =>
        NativeMethods.SendMessageTimeout(
            NativeMethods.HWndBroadcast,
            NativeMethods.WmSettingChange,
            IntPtr.Zero,
            "Software\\Microsoft\\Clipboard",
            NativeMethods.SmtoAbortIfHung,
            NativeMethods.NotifyTimeoutMilliseconds,
            out _);
}
