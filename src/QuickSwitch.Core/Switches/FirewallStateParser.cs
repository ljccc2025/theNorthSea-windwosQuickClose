namespace QuickSwitch.Core.Switches;

public static class FirewallStateParser
{
    private static readonly string[] RequiredProfiles = ["Domain", "Private", "Public"];

    private static readonly Dictionary<string, string> DisplayNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Domain"] = "域",
            ["Private"] = "专用",
            ["Public"] = "公用",
        };

    public static SwitchReadResult Parse(string? standardOutput)
    {
        var raw = (standardOutput ?? string.Empty).Trim();
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var chunk in raw.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = chunk.IndexOf('=');
            if (separator <= 0) continue;

            var name = chunk[..separator].Trim();
            var value = chunk[(separator + 1)..].Trim();
            if (name.Length == 0 || !TryParseFlag(value, out var enabled)) continue;

            states[name] = enabled;
        }

        var missing = RequiredProfiles.Where(profile => !states.ContainsKey(profile)).ToArray();
        if (missing.Length > 0)
        {
            var reason = raw.Length == 0
                ? "未读取到任何输出"
                : $"缺少档位 {string.Join(", ", missing)}，原始输出：{raw}";
            return new SwitchReadResult(SwitchState.Unknown, reason);
        }

        var enabledProfiles = RequiredProfiles.Where(profile => states[profile]).ToArray();
        if (enabledProfiles.Length == 0)
            return new SwitchReadResult(SwitchState.Off, "域 / 专用 / 公用 三档全部关闭");

        if (enabledProfiles.Length == RequiredProfiles.Length)
            return new SwitchReadResult(SwitchState.On, "域 / 专用 / 公用 三档全部开启");

        var names = string.Join(" / ", enabledProfiles.Select(DisplayName));
        return new SwitchReadResult(SwitchState.Mixed, $"已开启：{names}");
    }

    private static string DisplayName(string profile) =>
        DisplayNames.TryGetValue(profile, out var display) ? display : profile;

    private static bool TryParseFlag(string value, out bool enabled)
    {
        switch (value.ToLowerInvariant())
        {
            case "true":
            case "1":
            case "on":
            case "yes":
                enabled = true;
                return true;
            case "false":
            case "0":
            case "off":
            case "no":
                enabled = false;
                return true;
            default:
                enabled = false;
                return false;
        }
    }
}
