using System.Runtime.InteropServices;

namespace QuickSwitch.Core.Infrastructure;

/// 进程层之外的唯一 P/Invoke 集中地：令牌查询 + 设置变更广播。
internal static class NativeMethods
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint TokenQuery = 0x0008;
    internal const int TokenUser = 1;

    internal const int InternetOptionRefresh = 37;
    internal const int InternetOptionSettingsChanged = 39;

    internal const uint WmSettingChange = 0x001A;
    internal const uint SmtoAbortIfHung = 0x0002;
    internal const uint NotifyTimeoutMilliseconds = 1000;
    internal static readonly IntPtr HWndBroadcast = new(0xFFFF);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("wininet.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InternetSetOption(IntPtr internet, int option, IntPtr buffer, int bufferLength);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        string? lParam,
        uint flags,
        uint timeout,
        out IntPtr result);
}
