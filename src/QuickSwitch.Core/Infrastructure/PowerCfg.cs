using System.Text;

namespace QuickSwitch.Core.Infrastructure;

/// powercfg 出口。开关只依赖这个接口，单测里塞假实现就能验全套分支。
public interface IPowerCfg
{
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// powercfg 出口。刻意绕道 PowerShell，不走 ProcessRunner 直启 powercfg.exe：
/// 直接启动时 powercfg 继承不到控制台输出代码页，在中文系统上按 CP936 吐出「现有电源使用方案」，
/// 而 ProcessRunner 固定按 UTF-8 解码 → 方案名全成乱码，规格 §6 的「名字优先、GUID 兜底」静默反转，
/// 失败回显也给用户看乱码。PowerShellRunner 的 preamble 把输出编码钉成 UTF-8，子进程继承它，
/// 实测同一命令返回正确中文（`电源方案 GUID: 381b4222-…  (平衡) *`）。
public sealed class PowerCfg : IPowerCfg
{
    public const string ExecutableName = "powercfg.exe";

    private readonly PowerShellRunner _runner;

    public PowerCfg(PowerShellRunner runner) => _runner = runner;

    public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        _runner.RunAsync(BuildScript(arguments), cancellationToken);

    /// 逐参数单引号包裹（内部单引号翻倍），不用字符串拼命令行：中文方案名、GUID、斜杠都原样过去。
    public static string BuildScript(IReadOnlyList<string> arguments)
    {
        var script = new StringBuilder("& ").Append(ExecutableName);

        foreach (var argument in arguments)
            script.Append(' ').Append('\'').Append(argument.Replace("'", "''")).Append('\'');

        return script.ToString();
    }
}
