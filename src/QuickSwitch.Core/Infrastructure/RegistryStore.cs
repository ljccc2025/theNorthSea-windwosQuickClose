using Microsoft.Win32;

namespace QuickSwitch.Core.Infrastructure;

/// 注册表根：用户级开关走 HKCU，电源类开关走 HKLM。
public enum RegistryScope
{
    CurrentUser,
    LocalMachine,
}

/// 注册表读写口子。抽出来是为了让开关单测不碰真机注册表。
public interface IRegistryStore
{
    int? ReadDword(RegistryScope scope, string subKeyPath, string valueName);

    string? ReadString(RegistryScope scope, string subKeyPath, string valueName);

    void WriteDword(RegistryScope scope, string subKeyPath, string valueName, int value);
}

public sealed class WindowsRegistryStore : IRegistryStore
{
    public int? ReadDword(RegistryScope scope, string subKeyPath, string valueName)
    {
        using var key = Resolve(scope).OpenSubKey(subKeyPath);
        return key?.GetValue(valueName) is int value ? value : null;
    }

    public string? ReadString(RegistryScope scope, string subKeyPath, string valueName)
    {
        using var key = Resolve(scope).OpenSubKey(subKeyPath);
        return key?.GetValue(valueName) as string;
    }

    public void WriteDword(RegistryScope scope, string subKeyPath, string valueName, int value)
    {
        using var key = Resolve(scope).CreateSubKey(subKeyPath, writable: true)
            ?? throw new InvalidOperationException($"无法打开注册表键 {HiveName(scope)}\\{subKeyPath}。");

        key.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    private static RegistryKey Resolve(RegistryScope scope) => scope switch
    {
        RegistryScope.CurrentUser => Registry.CurrentUser,
        RegistryScope.LocalMachine => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "未知的注册表根。"),
    };

    private static string HiveName(RegistryScope scope) => scope switch
    {
        RegistryScope.CurrentUser => "HKCU",
        RegistryScope.LocalMachine => "HKLM",
        _ => "?",
    };
}
