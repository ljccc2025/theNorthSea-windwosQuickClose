namespace QuickSwitch.Core.Switches;

/// 一条配置 = 一个功能开关实例。加功能只改这张表，不改代码（规格 §6「一类多实例」）。
public sealed record WindowsFeatureSpec(string FeatureName, string Id, string Title, string Subtitle);

public static class WindowsFeatureCatalog
{
    public static IReadOnlyList<WindowsFeatureSpec> All { get; } =
    [
        new("Microsoft-Hyper-V-All", "windows-feature:hyper-v", "Hyper-V", "虚拟机平台（含管理工具），关闭后虚拟机不可用"),
        new("Microsoft-Windows-Subsystem-Linux", "windows-feature:wsl", "WSL", "适用于 Linux 的 Windows 子系统"),
        new("VirtualMachinePlatform", "windows-feature:vm-platform", "虚拟机平台", "WSL2 与沙盒依赖的虚拟机平台"),
    ];
}

