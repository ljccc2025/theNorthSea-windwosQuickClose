namespace QuickSwitch.Core.Infrastructure;

public static class ErrorText
{
    public const string Fallback = "命令执行失败，且未返回错误信息。";

    public static string FirstLine(string? text, string fallback = Fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        return text
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0) ?? fallback;
    }
}
