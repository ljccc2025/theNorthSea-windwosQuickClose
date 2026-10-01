namespace QuickSwitch.Core.Switches;

/// 解析 Get-MpComputerStatus 取值：`实时防护|篡改防护`（脚本按这个格式拼一行）。
/// 篡改防护开着时 Windows 拒绝一切外部修改，卡片如实报 Blocked，而不是假装能写。
public static class DefenderStatusParser
{
    public const string TamperDetail = "篡改防护已开启，Windows 拒绝外部修改实时防护，请到 Windows 安全中心手动切换";

    public static SwitchReadResult Parse(string? output)
    {
        var text = output?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return new SwitchReadResult(SwitchState.Unknown, "Defender 没有返回状态");

        var parts = text.Split('|');
        if (parts.Length < 2
            || !bool.TryParse(parts[0].Trim(), out var realTime)
            || !bool.TryParse(parts[1].Trim(), out var tamperProtected))
        {
            return new SwitchReadResult(SwitchState.Unknown, $"无法解析 Defender 状态：{text}");
        }

        if (tamperProtected)
            return new SwitchReadResult(SwitchState.Blocked, TamperDetail);

        return realTime
            ? new SwitchReadResult(SwitchState.On, "实时防护已开启")
            : new SwitchReadResult(SwitchState.Off, "实时防护已关闭，Defender 不再实时拦截");
    }
}

