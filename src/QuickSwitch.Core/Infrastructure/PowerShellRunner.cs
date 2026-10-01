namespace QuickSwitch.Core.Infrastructure;

public sealed class PowerShellRunner
{
    /// 子进程若不先改 OutputEncoding，中文系统上会按 GBK 写出，
    /// 而我们按 UTF-8 解码，结果就是乱码。所有脚本前缀这段。
    public const string Preamble = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop';";

    /// 原生命令（netsh、cmd 等）的退出码不会自动变成 powershell.exe 的退出码，
    /// 末尾显式 exit $LASTEXITCODE 才能把真实退出码传出来；纯 cmdlet 路径
    /// $LASTEXITCODE 为 $null，exit $null 仍按 0 退出。
    public const string ExitCodePassthrough = "; exit $LASTEXITCODE";

    private readonly IProcessRunner _runner;

    public PowerShellRunner(IProcessRunner runner) => _runner = runner;

    public Task<ProcessResult> RunAsync(string script, CancellationToken cancellationToken, TimeSpan? timeout = null) =>
        _runner.RunAsync(
            "powershell.exe",
            [
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command", Preamble + script + ExitCodePassthrough,
            ],
            cancellationToken,
            timeout);
}
