namespace QuickSwitch.Core.Infrastructure;

public sealed class PowerShellRunner
{
    /// 子进程若不先改 OutputEncoding，中文系统上会按 GBK 写出，
    /// 而我们按 UTF-8 解码，结果就是乱码。所有脚本前缀这段。
    public const string Preamble = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop';";

    private readonly IProcessRunner _runner;

    public PowerShellRunner(IProcessRunner runner) => _runner = runner;

    public Task<ProcessResult> RunAsync(string script, CancellationToken cancellationToken) =>
        _runner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command", Preamble + script,
            ],
            cancellationToken);
}
