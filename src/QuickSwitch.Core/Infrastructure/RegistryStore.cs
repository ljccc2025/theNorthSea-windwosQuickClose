using Microsoft.Win32;

namespace QuickSwitch.Core.Infrastructure;

/// 当前用户配置单元（HKCU）的读写口子。抽出来是为了让开关单测不碰真机注册表。
public interface IRegistryStore
{
    int? ReadDword(string subKeyPath, string valueName);

    string? ReadString(string subKeyPath, string valueName);

    void WriteDword(string subKeyPath, string valueName, int value);
}

public sealed class WindowsRegistryStore : IRegistryStore
{
    public int? ReadDword(string subKeyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath);
        return key?.GetValue(valueName) is int value ? value : null;
    }

    public string? ReadString(string subKeyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath);
        return key?.GetValue(valueName) as string;
    }

    public void WriteDword(string subKeyPath, string valueName, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表键 HKCU\\{subKeyPath}。");

        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }
}
