namespace QuickSwitch.Core.Switches;

/// Get-WindowsOptionalFeature 的 State 只有四个取值，逐字映射，认不出来就报未知。
public static class WindowsFeatureStateParser
{
    public static SwitchReadResult Parse(string? featureName, string? output)
    {
        var state = output?.Trim();
        if (string.IsNullOrWhiteSpace(state))
            return new SwitchReadResult(SwitchState.Unknown, $"{featureName} 没有返回状态");

        return state switch
        {
            "Enabled" => new SwitchReadResult(SwitchState.On, $"{featureName} 已启用"),
            "Disabled" => new SwitchReadResult(SwitchState.Off, $"{featureName} 已禁用"),
            "EnablePending" => new SwitchReadResult(SwitchState.PendingRestart, $"{featureName} 将于重启后启用"),
            "DisablePending" => new SwitchReadResult(SwitchState.PendingRestart, $"{featureName} 将于重启后禁用"),
            _ => new SwitchReadResult(SwitchState.Unknown, $"无法识别的功能状态：{state}"),
        };
    }
}

