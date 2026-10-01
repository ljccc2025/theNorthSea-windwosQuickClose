using System.Text.RegularExpressions;

namespace QuickSwitch.Core.Infrastructure;

/// 一条电源方案：GUID + 显示名 + 是否是当前活动方案。
public sealed record PowerScheme(string Guid, string Name, bool IsActive);

/// powercfg 输出解析。纯函数，中英文输出都要认——本机是中文系统，但英文样本在测试里同样要过。
public static partial class PowerSchemeParser
{
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
    public const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

    /// UI 上固定三选一；GUID 由 /list 名字匹配动态解析，匹配不到才回落到内置常量。
    public static readonly IReadOnlyList<string> CanonicalOptions = ["高性能", "平衡", "节能"];

    [GeneratedRegex(@"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\((?<name>[^()]*)\)\s*\*?\s*$")]
    private static partial Regex TrailingNamePattern();

    /// `powercfg /getactivescheme` → 活动方案 GUID（小写）。解析不出来返回 null。
    public static string? ParseActiveGuid(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;

        var match = GuidPattern().Match(stdout);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    /// `powercfg /getactivescheme` → 括号里的显示名（中英文都靠它）。没有括号返回 null。
    public static string? ParseActiveName(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;

        foreach (var line in SplitLines(stdout))
        {
            var match = TrailingNamePattern().Match(line);
            if (match.Success) return match.Groups["name"].Value.Trim();
        }

        return null;
    }

    /// `powercfg /list` → 全部方案。带 `*` 的那条是当前活动方案。
    public static IReadOnlyList<PowerScheme> ParseList(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return [];

        var schemes = new List<PowerScheme>();

        foreach (var line in SplitLines(stdout))
        {
            var guidMatch = GuidPattern().Match(line);
            if (!guidMatch.Success) continue;

            var guid = guidMatch.Value.ToLowerInvariant();
            var nameMatch = TrailingNamePattern().Match(line);
            var name = nameMatch.Success ? nameMatch.Groups["name"].Value.Trim() : guid;
            var isActive = line.TrimEnd().EndsWith('*');

            schemes.Add(new PowerScheme(guid, name, isActive));
        }

        return schemes;
    }

    /// 方案名 → 内置 GUID 兜底常量；非三选一方案返回 null。
    public static string? CanonicalGuid(string optionName) => optionName?.Trim() switch
    {
        "高性能" => HighPerformanceGuid,
        "平衡" => BalancedGuid,
        "节能" => PowerSaverGuid,
        _ => null,
    };

    /// 选项名与系统显示的方案名是否同一个方案（中英文资源名都认）。
    public static bool MatchesOption(string optionName, string schemeName)
    {
        var candidate = schemeName?.Trim();
        if (string.IsNullOrEmpty(candidate)) return false;

        return SynonymsFor(optionName).Any(synonym => string.Equals(synonym, candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] SynonymsFor(string? optionName) => optionName?.Trim() switch
    {
        "高性能" => ["高性能", "High performance"],
        "平衡" => ["平衡", "Balanced"],
        "节能" => ["节能", "Power saver"],
        _ => [],
    };

    private static IEnumerable<string> SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
}
